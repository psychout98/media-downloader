using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Configuration;
using MediaDownloader.Api.Data.Entities;
using MediaDownloader.Api.Data.Repositories;
using MediaDownloader.Shared.Enums;
using MediaDownloader.Shared.Models;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Services;

public class JobProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger<JobProcessorService> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeJobs = new();
    private SemaphoreSlim _semaphore = null!;

    public JobProcessorService(IServiceScopeFactory scopeFactory, IOptionsMonitor<AppSettings> settings, ILogger<JobProcessorService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _semaphore = new SemaphoreSlim(_settings.CurrentValue.Media.MaxConcurrentDownloads);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
                var pendingJobs = await jobRepo.GetPendingAsync();

                foreach (var job in pendingJobs)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    _activeJobs[job.Id] = cts;

                    _ = Task.Run(async () =>
                    {
                        await _semaphore.WaitAsync(cts.Token);
                        try
                        {
                            await ProcessJobAsync(job.Id, cts.Token);
                        }
                        finally
                        {
                            _semaphore.Release();
                            _activeJobs.TryRemove(job.Id, out _);
                        }
                    }, cts.Token);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in job processor loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessJobAsync(string jobId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var titleRepo = scope.ServiceProvider.GetRequiredService<ITitleRepository>();
        var mediaItemRepo = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();
        var rdClient = scope.ServiceProvider.GetRequiredService<RealDebridClient>();
        var tmdbClient = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        var organizer = scope.ServiceProvider.GetRequiredService<MediaOrganizer>();
        var downloadService = scope.ServiceProvider.GetRequiredService<FileDownloadService>();

        var job = await jobRepo.GetByIdAsync(jobId);
        if (job == null) return;

        try
        {
            // 1. Deserialize StreamData
            await UpdateJobStatusAsync(jobRepo, job, JobStatus.Searching);
            await jobRepo.AppendLogAsync(jobId, "Parsing stream data...");

            var streamData = JsonSerializer.Deserialize<StreamData>(job.StreamData ?? "{}", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (streamData?.Stream?.InfoHash == null)
                throw new InvalidOperationException("Invalid stream data");

            var media = new MediaInfo(
                streamData.Media.TmdbId, streamData.Media.Title, streamData.Media.Year,
                streamData.Media.Type, streamData.Media.IsAnime, streamData.Media.ImdbId,
                streamData.Media.PosterPath, streamData.Media.Season, streamData.Media.Episode,
                streamData.Media.Overview);

            await UpdateJobStatusAsync(jobRepo, job, JobStatus.Found);
            job.TorrentName = streamData.Stream.Name;
            job.SizeBytes = streamData.Stream.SizeBytes;
            await jobRepo.UpdateAsync(job);

            // 2. Add magnet to Real-Debrid
            await UpdateJobStatusAsync(jobRepo, job, JobStatus.AddingToRd);
            await jobRepo.AppendLogAsync(jobId, $"Adding magnet to Real-Debrid: {streamData.Stream.InfoHash}");

            var magnetLink = $"magnet:?xt=urn:btih:{streamData.Stream.InfoHash}";
            var torrentId = await rdClient.AddMagnetAsync(magnetLink);
            job.RdTorrentId = torrentId;
            await jobRepo.UpdateAsync(job);

            await rdClient.SelectAllFilesAsync(torrentId);

            // 3. Wait for RD to process
            await UpdateJobStatusAsync(jobRepo, job, JobStatus.WaitingForRd);
            await jobRepo.AppendLogAsync(jobId, "Waiting for Real-Debrid to process...");

            var progress = new Progress<int>(async pct =>
            {
                job.Progress = pct / 100.0;
                try { await jobRepo.UpdateAsync(job); } catch { /* best effort */ }
            });

            var links = await rdClient.WaitUntilDownloadedAsync(torrentId, progress, ct);

            // 4. Unrestrict links
            var unrestrictedFiles = await rdClient.UnrestrictAllAsync(links);

            // 5. Download files
            await UpdateJobStatusAsync(jobRepo, job, JobStatus.Downloading);
            var stagingDir = Path.Combine(_settings.CurrentValue.Media.AppDataDir, "staging", jobId);
            Directory.CreateDirectory(stagingDir);

            var videoFiles = unrestrictedFiles.Where(f => IsVideoUrl(f.Url)).ToList();
            if (videoFiles.Count == 0)
                videoFiles = unrestrictedFiles; // fallback to all files

            long totalSize = videoFiles.Sum(f => f.Size);
            long totalDownloaded = 0;

            var downloadedFiles = new List<(string Path, string Url, long Size)>();

            foreach (var (url, size) in videoFiles)
            {
                ct.ThrowIfCancellationRequested();

                var fileName = FilenameFromUrl(url) ?? $"file_{downloadedFiles.Count}{GuessExtension(url)}";
                var destPath = Path.Combine(stagingDir, fileName);

                await jobRepo.AppendLogAsync(jobId, $"Downloading: {fileName}");

                var downloadProgress = new Progress<(long downloaded, long total)>(d =>
                {
                    job.DownloadedBytes = totalDownloaded + d.downloaded;
                    job.Progress = totalSize > 0 ? (double)job.DownloadedBytes / totalSize : 0;
                    try { jobRepo.UpdateAsync(job).GetAwaiter().GetResult(); } catch { /* best effort */ }
                });

                await downloadService.DownloadFileAsync(url, destPath, downloadProgress, ct);
                totalDownloaded += size;
                downloadedFiles.Add((destPath, url, size));
            }

            // 6. Organize files
            await UpdateJobStatusAsync(jobRepo, job, JobStatus.Organizing);

            if (downloadedFiles.Count == 1)
            {
                // Single file torrent
                await jobRepo.AppendLogAsync(jobId, "Organizing single file...");
                var dest = await organizer.OrganizeAsync(stagingDir, media);

                // Create/update media item
                var mediaItem = (await mediaItemRepo.GetByJobIdAsync(jobId)).FirstOrDefault();
                if (mediaItem != null)
                {
                    mediaItem.FilePath = dest;
                    await mediaItemRepo.UpdateAsync(mediaItem);
                }
            }
            else
            {
                // Season pack — multiple video files
                await jobRepo.AppendLogAsync(jobId, $"Organizing {downloadedFiles.Count} files (season pack)...");

                foreach (var (filePath, url, size) in downloadedFiles)
                {
                    var fName = Path.GetFileNameWithoutExtension(filePath);
                    var epNum = EpisodeFromFilename(fName);
                    string? epTitle = null;

                    if (epNum.HasValue && media.TmdbId > 0)
                    {
                        epTitle = await tmdbClient.GetEpisodeTitleAsync(media.TmdbId, media.Season ?? 1, epNum.Value);
                    }

                    var dest = await organizer.OrganizeAsync(filePath, media, epNum, epTitle);

                    // Find or create matching media item
                    var mediaItems = await mediaItemRepo.GetByJobIdAsync(jobId);
                    var item = epNum.HasValue
                        ? mediaItems.FirstOrDefault(m => m.Episode == epNum)
                        : mediaItems.FirstOrDefault(m => m.FilePath == null);

                    if (item != null)
                    {
                        item.FilePath = dest;
                        item.Episode = epNum;
                        item.EpisodeTitle = epTitle;
                        await mediaItemRepo.UpdateAsync(item);
                    }
                }
            }

            // 7. Fetch poster
            await SavePosterAsync(media.PosterPath, job.TitleId);
            await jobRepo.AppendLogAsync(jobId, "Poster saved.");

            // 8. Mark complete
            await UpdateJobStatusAsync(jobRepo, job, JobStatus.Complete);
            job.Progress = 1.0;
            await jobRepo.UpdateAsync(job);
            await jobRepo.AppendLogAsync(jobId, "Job complete!");

            // Cleanup staging
            CleanupStaging(jobId);
        }
        catch (OperationCanceledException)
        {
            job.JobStatus = JobStatus.Cancelled;
            await jobRepo.UpdateAsync(job);
            await jobRepo.AppendLogAsync(jobId, "Job cancelled.");
            CleanupStaging(jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            job.JobStatus = JobStatus.Failed;
            job.Error = ex.Message;
            await jobRepo.UpdateAsync(job);
            await jobRepo.AppendLogAsync(jobId, $"Error: {ex.Message}");
            CleanupStaging(jobId);
        }
    }

    private static async Task UpdateJobStatusAsync(IJobRepository jobRepo, Job job, JobStatus status)
    {
        job.JobStatus = status;
        await jobRepo.UpdateAsync(job);
    }

    public bool CancelJob(string jobId)
    {
        if (_activeJobs.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    public void CleanupStaging(string jobId)
    {
        try
        {
            var stagingDir = Path.Combine(_settings.CurrentValue.Media.AppDataDir, "staging", jobId);
            if (Directory.Exists(stagingDir))
                Directory.Delete(stagingDir, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup staging for job {JobId}", jobId);
        }
    }

    private async Task SavePosterAsync(string? posterPath, string titleId)
    {
        if (string.IsNullOrEmpty(posterPath)) return;

        var posterDir = Path.Combine(_settings.CurrentValue.Media.AppDataDir, "posters");
        var posterFile = Path.Combine(posterDir, $"{SafePosterKey(titleId)}.jpg");

        if (File.Exists(posterFile)) return;

        try
        {
            Directory.CreateDirectory(posterDir);
            using var httpClient = new HttpClient();
            var data = await httpClient.GetByteArrayAsync($"https://image.tmdb.org/t/p/w500{posterPath}");
            await File.WriteAllBytesAsync(posterFile, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save poster for title {TitleId}", titleId);
        }
    }

    // --- Helper utils ---

    public static string? FilenameFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return null;

        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            if (path.EndsWith('/')) return null;

            var decoded = HttpUtility.UrlDecode(Path.GetFileName(path));
            if (string.IsNullOrEmpty(decoded) || !Path.HasExtension(decoded)) return null;

            return decoded;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsVideoUrl(string url)
    {
        var filename = FilenameFromUrl(url);
        if (filename == null) return false;

        var ext = Path.GetExtension(filename).ToLowerInvariant();
        return ext is ".mkv" or ".mp4" or ".avi" or ".m4v" or ".wmv";
    }

    public static int? EpisodeFromFilename(string filename)
    {
        // S01E03, s02e10
        var match = Regex.Match(filename, @"[Ss]\d{1,2}[Ee](\d{1,3})");
        if (match.Success) return int.Parse(match.Groups[1].Value);

        // E05, Ep03
        match = Regex.Match(filename, @"(?:^|[\s._-])(?:E|Ep)(\d{1,3})(?:[\s._-]|$)", RegexOptions.IgnoreCase);
        if (match.Success) return int.Parse(match.Groups[1].Value);

        // Anime pattern: " - 12" or " - 012"
        match = Regex.Match(filename, @"\s-\s(\d{2,3})(?:\s|$|[\.\[])");
        if (match.Success) return int.Parse(match.Groups[1].Value);

        return null;
    }

    public static string SafePosterKey(string key)
    {
        return Regex.Replace(key, @"[<>:""/\\|?*]", "");
    }

    private static string GuessExtension(string url)
    {
        var filename = FilenameFromUrl(url);
        return filename != null ? Path.GetExtension(filename) : ".mkv";
    }
}
