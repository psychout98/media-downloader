using MediaDownloader.Api.Configuration;
using MediaDownloader.Api.Data.Repositories;
using MediaDownloader.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Controllers;

[ApiController]
public class LibraryController : ControllerBase
{
    private readonly ITitleRepository _titleRepo;
    private readonly IMediaItemRepository _mediaItemRepo;
    private readonly LibraryManager _libraryManager;
    private readonly ProgressService _progressService;
    private readonly IOptionsMonitor<AppSettings> _settings;

    public LibraryController(
        ITitleRepository titleRepo,
        IMediaItemRepository mediaItemRepo,
        LibraryManager libraryManager,
        ProgressService progressService,
        IOptionsMonitor<AppSettings> settings)
    {
        _titleRepo = titleRepo;
        _mediaItemRepo = mediaItemRepo;
        _libraryManager = libraryManager;
        _progressService = progressService;
        _settings = settings;
    }

    [HttpGet("/api/library")]
    public async Task<IActionResult> GetLibrary(
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] bool force = false)
    {
        if (force)
            await _libraryManager.IndexAsync();

        var titles = await _titleRepo.GetAllAsync(type, search);

        var items = new List<object>();
        foreach (var title in titles)
        {
            var mediaItems = await _mediaItemRepo.GetByTitleIdAsync(title.Id, includeArchived: true);
            var totalSize = mediaItems
                .Where(m => m.FilePath != null)
                .Sum(m =>
                {
                    try { return new FileInfo(m.FilePath!).Length; }
                    catch { return 0L; }
                });

            items.Add(new
            {
                id = title.Id,
                tmdbId = title.TmdbId,
                imdbId = title.ImdbId,
                title = title.Name,
                year = title.Year,
                type = title.Type,
                isAnime = title.IsAnime,
                overview = title.Overview,
                posterPath = title.PosterPath,
                folderName = title.FolderName,
                fileCount = mediaItems.Count(m => m.FilePath != null),
                totalSize,
                hasArchived = mediaItems.Any(m => m.IsArchived)
            });
        }

        return Ok(new { items, count = items.Count });
    }

    [HttpPost("/api/library/refresh")]
    public async Task<IActionResult> RefreshLibrary()
    {
        var result = await _libraryManager.RefreshAsync();
        return Ok(new
        {
            renamed = result.Renamed,
            postersFetched = result.PostersFetched,
            errors = result.Errors,
            totalItems = result.TotalItems
        });
    }

    [HttpGet("/api/library/poster")]
    public async Task<IActionResult> GetPoster([FromQuery] string titleId)
    {
        var posterDir = Path.Combine(_settings.CurrentValue.Media.AppDataDir, "posters");
        var posterFile = Path.Combine(posterDir, $"{titleId}.jpg");

        if (!System.IO.File.Exists(posterFile))
        {
            // Try to fetch from TMDB
            var title = await _titleRepo.GetByIdAsync(titleId);
            if (title?.PosterPath == null)
                return NotFound(new { error = "not_found", detail = "No poster available" });

            try
            {
                Directory.CreateDirectory(posterDir);
                using var httpClient = new HttpClient();
                var data = await httpClient.GetByteArrayAsync($"https://image.tmdb.org/t/p/w500{title.PosterPath}");
                await System.IO.File.WriteAllBytesAsync(posterFile, data);
            }
            catch
            {
                return NotFound(new { error = "not_found", detail = "Failed to fetch poster from TMDB" });
            }
        }

        var bytes = await System.IO.File.ReadAllBytesAsync(posterFile);
        return File(bytes, "image/jpeg");
    }

    [HttpGet("/api/library/episodes")]
    public async Task<IActionResult> GetEpisodes(
        [FromQuery] string titleId,
        [FromQuery] bool includeArchived = false)
    {
        var title = await _titleRepo.GetByIdAsync(titleId);
        if (title == null)
            return NotFound(new { error = "not_found", detail = "Title not found" });

        var mediaItems = await _mediaItemRepo.GetByTitleIdAsync(titleId, includeArchived);

        var seasons = mediaItems
            .Where(m => m.Season.HasValue)
            .GroupBy(m => m.Season!.Value)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                season = g.Key,
                episodes = g.OrderBy(m => m.Episode).Select(m => new
                {
                    mediaItemId = m.Id,
                    episode = m.Episode,
                    episodeTitle = m.EpisodeTitle,
                    fileName = m.FilePath != null ? Path.GetFileName(m.FilePath) : null,
                    filePath = m.FilePath,
                    isArchived = m.IsArchived,
                    progress = m.WatchProgress != null ? new
                    {
                        positionMs = m.WatchProgress.PositionMs,
                        durationMs = m.WatchProgress.DurationMs,
                        watched = m.WatchProgress.Watched
                    } : null
                }).ToList()
            }).ToList();

        return Ok(new { seasons });
    }

    [HttpGet("/api/progress")]
    public async Task<IActionResult> GetProgress([FromQuery] string mediaItemId)
    {
        var progress = await _progressService.GetProgressAsync(mediaItemId);
        if (progress == null)
            return Ok(new { positionMs = 0, durationMs = 0, watched = false });

        return Ok(new
        {
            positionMs = progress.PositionMs,
            durationMs = progress.DurationMs,
            watched = progress.Watched
        });
    }

    [HttpPost("/api/progress")]
    public async Task<IActionResult> SaveProgress([FromBody] SaveProgressRequest request)
    {
        await _progressService.SaveProgressAsync(request.MediaItemId, request.PositionMs, request.DurationMs);
        return Ok(new { ok = true });
    }
}

public record SaveProgressRequest(string MediaItemId, long PositionMs, long DurationMs);
