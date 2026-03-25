using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Configuration;
using MediaDownloader.Api.Data.Entities;
using MediaDownloader.Api.Data.Repositories;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Services;

public class WatchTrackerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger<WatchTrackerService> _logger;

    private readonly ConcurrentDictionary<string, double> _maxPosition = new();
    private string? _prevFile;
    private int _stoppedCount;

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".srt", ".ass", ".ssa", ".sub", ".idx", ".vtt" };

    private static HashSet<string> VideoExtensions => Shared.Constants.VideoExtensions.All;

    public WatchTrackerService(IServiceScopeFactory scopeFactory, IOptionsMonitor<AppSettings> settings, ILogger<WatchTrackerService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in watch tracker tick");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task TickAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var mpcClient = scope.ServiceProvider.GetRequiredService<MpcClient>();
        var status = await mpcClient.GetStatusAsync();

        if (!status.Reachable)
        {
            _stoppedCount++;
            if (_stoppedCount >= 2 && _prevFile != null)
            {
                await OnStoppedAsync(_prevFile);
                _prevFile = null;
                _stoppedCount = 0;
            }
            return;
        }

        var currentFile = status.FilePath;

        if (status.IsPlaying && currentFile != null)
        {
            _stoppedCount = 0;

            // Track max position as percentage
            if (status.Duration > 0)
            {
                var pct = (double)status.Position / status.Duration;
                _maxPosition.AddOrUpdate(currentFile, pct, (_, existing) => Math.Max(existing, pct));
            }

            // Save progress to DB
            await SaveProgressAsync(currentFile, status.Position, status.Duration);

            // Check for file change
            if (_prevFile != null && _prevFile != currentFile)
            {
                await OnStoppedAsync(_prevFile);
            }

            _prevFile = currentFile;
        }
        else if (status.State == 0) // Stopped (not paused)
        {
            _stoppedCount++;
            if (_stoppedCount >= 2 && _prevFile != null)
            {
                await OnStoppedAsync(_prevFile);
                _prevFile = null;
                _stoppedCount = 0;
            }
        }
        else
        {
            // Paused — reset stopped counter
            _stoppedCount = 0;
        }
    }

    private async Task SaveProgressAsync(string filePath, long positionMs, long durationMs)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediaItem = await LookupMediaItemAsync(scope, filePath);
            if (mediaItem == null) return;

            var progressRepo = scope.ServiceProvider.GetRequiredService<IProgressRepository>();
            await progressRepo.SaveAsync(mediaItem.Id, positionMs, durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to save progress for {File}", filePath);
        }
    }

    private async Task OnStoppedAsync(string filePath)
    {
        var threshold = _settings.CurrentValue.Media.WatchThreshold;

        if (!_maxPosition.TryGetValue(filePath, out var maxPct))
            return;

        _maxPosition.TryRemove(filePath, out _);

        if (maxPct < threshold)
        {
            _logger.LogDebug("File {File} watched {Pct:P0}, below threshold", filePath, maxPct);
            return;
        }

        _logger.LogInformation("File {File} watched {Pct:P0}, archiving...", filePath, maxPct);
        await ArchiveFileAsync(filePath);
    }

    public async Task ArchiveFileAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        var archiveDir = _settings.CurrentValue.Media.ArchiveDir;
        var moviesDir = _settings.CurrentValue.Media.MoviesDir;
        var tvDir = _settings.CurrentValue.Media.TvDir;

        // Skip if not in media directories
        if (!filePath.StartsWith(moviesDir, StringComparison.OrdinalIgnoreCase) &&
            !filePath.StartsWith(tvDir, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skipping archive for file outside media dirs: {File}", filePath);
            return;
        }

        try
        {
            // Determine archive destination
            var fileName = Path.GetFileName(filePath);
            var parentDir = Path.GetDirectoryName(filePath)!;
            var parentName = Path.GetFileName(parentDir);
            var archivePath = Path.Combine(archiveDir, parentName, fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);

            // Move video file
            if (File.Exists(archivePath)) File.Delete(archivePath);
            File.Move(filePath, archivePath);

            // Move subtitle files with same base name
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            foreach (var file in Directory.EnumerateFiles(parentDir))
            {
                var ext = Path.GetExtension(file);
                if (SubtitleExtensions.Contains(ext) &&
                    Path.GetFileNameWithoutExtension(file).StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
                {
                    var subDest = Path.Combine(Path.GetDirectoryName(archivePath)!, Path.GetFileName(file));
                    if (File.Exists(subDest)) File.Delete(subDest);
                    File.Move(file, subDest);
                }
            }

            // Update DB
            using var scope = _scopeFactory.CreateScope();
            var mediaItem = await LookupMediaItemAsync(scope, filePath);
            if (mediaItem != null)
            {
                var mediaItemRepo = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();
                mediaItem.IsArchived = true;
                mediaItem.FilePath = archivePath;
                await mediaItemRepo.UpdateAsync(mediaItem);
            }

            // Clean empty source folders
            RemoveIfEmpty(parentDir);

            _logger.LogInformation("Archived {Source} → {Dest}", filePath, archivePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive {File}", filePath);
        }
    }

    public static string? ParseTitleIdFromPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var match = Regex.Match(path, @"\[(\d+)\]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<MediaItem?> LookupMediaItemAsync(IServiceScope scope, string filePath)
    {
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.MediaItems, m => m.FilePath == filePath);
    }

    public static void RemoveIfEmpty(string directory)
    {
        if (!Directory.Exists(directory)) return;

        // Check for video files in directory and subdirectories
        var hasVideos = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Any(f => VideoExtensions.Contains(Path.GetExtension(f)));

        if (!hasVideos)
        {
            try { Directory.Delete(directory, true); }
            catch { /* best effort */ }
        }
    }

    public static void MoveFolderRemnants(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;

        // Block if videos remain
        var hasVideos = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)
            .Any(f => VideoExtensions.Contains(Path.GetExtension(f)));
        if (hasVideos) return;

        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destPath = Path.Combine(destDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Move(file, destPath, true);
        }

        try { Directory.Delete(sourceDir, true); }
        catch { /* best effort */ }
    }
}
