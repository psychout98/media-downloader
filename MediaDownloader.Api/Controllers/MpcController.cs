using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Data;
using MediaDownloader.Api.Data.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace MediaDownloader.Api.Controllers;

[ApiController]
public class MpcController : ControllerBase
{
    private readonly MpcClient _mpcClient;
    private readonly TmdbClient _tmdbClient;
    private readonly IMediaItemRepository _mediaItemRepo;
    private readonly AppDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopeFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public MpcController(MpcClient mpcClient, TmdbClient tmdbClient, IMediaItemRepository mediaItemRepo, AppDbContext db, IMemoryCache cache, IServiceScopeFactory scopeFactory)
    {
        _mpcClient = mpcClient;
        _tmdbClient = tmdbClient;
        _mediaItemRepo = mediaItemRepo;
        _db = db;
        _cache = cache;
        _scopeFactory = scopeFactory;
    }

    [HttpGet("/api/mpc/status")]
    public async Task<IActionResult> GetStatus()
    {
        var status = await _mpcClient.GetStatusAsync();
        var enriched = await EnrichStatusAsync(status);
        return Ok(enriched);
    }

    [Route("/api/mpc/stream")]
    public async Task Stream()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = 400;
            return;
        }

        using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var ct = HttpContext.RequestAborted;

        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                // Create a new scope per iteration to avoid stale DbContext
                using var scope = _scopeFactory.CreateScope();
                var mpcClient = scope.ServiceProvider.GetRequiredService<MpcClient>();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var mediaItemRepo = scope.ServiceProvider.GetRequiredService<IMediaItemRepository>();
                var tmdbClient = scope.ServiceProvider.GetRequiredService<TmdbClient>();

                var status = await mpcClient.GetStatusAsync();
                var enriched = await EnrichStatusWithScopeAsync(status, db, mediaItemRepo, tmdbClient);

                var json = JsonSerializer.Serialize(enriched, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None); }
                catch { /* best effort */ }
            }
        }
    }

    [HttpPost("/api/mpc/command")]
    public async Task<IActionResult> SendCommand([FromBody] MpcCommandRequest request)
    {
        var commandId = ResolveCommand(request.Command);
        if (commandId == null)
            return BadRequest(new { error = "validation_error", detail = $"Unknown command: {request.Command}" });

        var success = await _mpcClient.SendCommandAsync(commandId.Value, request.PositionMs);
        return Ok(new { ok = success });
    }

    [HttpPost("/api/mpc/open")]
    public async Task<IActionResult> OpenFile([FromBody] MpcOpenRequest request)
    {
        var mediaItem = await _mediaItemRepo.GetByIdAsync(request.MediaItemId);
        if (mediaItem?.FilePath == null || !System.IO.File.Exists(mediaItem.FilePath))
            return NotFound(new { error = "not_found", detail = "Media file not found" });

        // If playlist provided, create .m3u and open that
        if (request.Playlist is { Count: > 0 })
        {
            var paths = new List<string> { mediaItem.FilePath };
            foreach (var pid in request.Playlist.Where(id => id != request.MediaItemId))
            {
                var pItem = await _mediaItemRepo.GetByIdAsync(pid);
                if (pItem?.FilePath != null && System.IO.File.Exists(pItem.FilePath))
                    paths.Add(pItem.FilePath);
            }

            // Write temporary .m3u playlist
            var tempDir = Path.Combine(Path.GetTempPath(), "MediaDownloader");
            Directory.CreateDirectory(tempDir);
            var m3uPath = Path.Combine(tempDir, $"playlist_{Guid.NewGuid():N}.m3u");
            await System.IO.File.WriteAllLinesAsync(m3uPath, paths);

            var success = await _mpcClient.OpenFileAsync(m3uPath);
            if (!success)
                return StatusCode(502, new { error = "mpc_unreachable", detail = "Failed to open playlist in MPC-BE" });

            return Ok(new { ok = true });
        }

        var opened = await _mpcClient.OpenFileAsync(mediaItem.FilePath);
        if (!opened)
            return StatusCode(502, new { error = "mpc_unreachable", detail = "Failed to open file in MPC-BE" });

        return Ok(new { ok = true });
    }

    [HttpPost("/api/mpc/next")]
    public async Task<IActionResult> NextEpisode()
    {
        var status = await _mpcClient.GetStatusAsync();
        if (!status.Reachable || string.IsNullOrEmpty(status.FilePath))
            return NotFound(new { error = "not_found", detail = "Nothing currently playing" });

        var currentItem = await _db.MediaItems
            .Include(m => m.Title)
            .FirstOrDefaultAsync(m => m.FilePath == status.FilePath);

        if (currentItem?.Season == null || currentItem.Episode == null)
            return NotFound(new { error = "not_found", detail = "Current file is not a TV episode" });

        var next = await _mediaItemRepo.FindAdjacentEpisodeAsync(
            currentItem.TitleId, currentItem.Season.Value, currentItem.Episode.Value, next: true);

        if (next?.FilePath == null)
            return NotFound(new { error = "not_found", detail = "No next episode found" });

        var success = await _mpcClient.OpenFileAsync(next.FilePath);
        return success
            ? Ok(new { ok = true, mediaItemId = next.Id, episode = next.Episode, episodeTitle = next.EpisodeTitle })
            : StatusCode(502, new { error = "mpc_unreachable", detail = "Failed to open next episode" });
    }

    [HttpPost("/api/mpc/prev")]
    public async Task<IActionResult> PrevEpisode()
    {
        var status = await _mpcClient.GetStatusAsync();
        if (!status.Reachable || string.IsNullOrEmpty(status.FilePath))
            return NotFound(new { error = "not_found", detail = "Nothing currently playing" });

        var currentItem = await _db.MediaItems
            .Include(m => m.Title)
            .FirstOrDefaultAsync(m => m.FilePath == status.FilePath);

        if (currentItem?.Season == null || currentItem.Episode == null)
            return NotFound(new { error = "not_found", detail = "Current file is not a TV episode" });

        var prev = await _mediaItemRepo.FindAdjacentEpisodeAsync(
            currentItem.TitleId, currentItem.Season.Value, currentItem.Episode.Value, next: false);

        if (prev?.FilePath == null)
            return NotFound(new { error = "not_found", detail = "No previous episode found" });

        var success = await _mpcClient.OpenFileAsync(prev.FilePath);
        return success
            ? Ok(new { ok = true, mediaItemId = prev.Id, episode = prev.Episode, episodeTitle = prev.EpisodeTitle })
            : StatusCode(502, new { error = "mpc_unreachable", detail = "Failed to open previous episode" });
    }

    private async Task<object> EnrichStatusAsync(MpcStatus status)
    {
        if (!status.Reachable)
        {
            return new
            {
                reachable = false,
                fileName = (string?)null,
                state = 0,
                isPlaying = false,
                isPaused = false,
                positionMs = 0L,
                durationMs = 0L,
                volume = 0,
                muted = false,
                titleId = (string?)null,
                title = (string?)null,
                isAnime = false,
                season = (int?)null,
                episode = (int?)null,
                episodeTitle = (string?)null,
                episodeCount = (int?)null,
                year = (int?)null,
                type = (string?)null,
                prevEpisode = (object?)null,
                nextEpisode = (object?)null
            };
        }

        // Look up current file in DB
        Data.Entities.MediaItem? currentItem = null;
        if (!string.IsNullOrEmpty(status.FilePath))
        {
            currentItem = await _db.MediaItems
                .Include(m => m.Title)
                .FirstOrDefaultAsync(m => m.FilePath == status.FilePath);
        }

        string? titleId = null, titleName = null, episodeTitle = null, type = null;
        int? season = null, episode = null, episodeCount = null, year = null;
        bool isAnime = false;
        object? prevEpisode = null, nextEpisode = null;

        if (currentItem?.Title != null)
        {
            titleId = currentItem.Title.Id;
            titleName = currentItem.Title.Name;
            type = currentItem.Title.Type;
            year = currentItem.Title.Year;
            isAnime = currentItem.Title.IsAnime;
            season = currentItem.Season;
            episode = currentItem.Episode;
            episodeTitle = currentItem.EpisodeTitle;

            // Get episode count (cached to avoid TMDB rate limits)
            if (season.HasValue && currentItem.Title.TmdbId.HasValue)
            {
                var cacheKey = $"epcount:{currentItem.Title.TmdbId}:{season}";
                if (!_cache.TryGetValue(cacheKey, out int cachedCount))
                {
                    cachedCount = await _tmdbClient.GetEpisodeCountAsync(currentItem.Title.TmdbId.Value, season.Value);
                    _cache.Set(cacheKey, cachedCount, TimeSpan.FromHours(1));
                }
                episodeCount = cachedCount;
            }

            // Get prev/next episodes
            if (season.HasValue && episode.HasValue)
            {
                var prev = await _mediaItemRepo.FindAdjacentEpisodeAsync(
                    currentItem.TitleId, season.Value, episode.Value, next: false);
                var next = await _mediaItemRepo.FindAdjacentEpisodeAsync(
                    currentItem.TitleId, season.Value, episode.Value, next: true);

                if (prev != null)
                    prevEpisode = new { mediaItemId = prev.Id, episode = prev.Episode, title = prev.EpisodeTitle };
                if (next != null)
                    nextEpisode = new { mediaItemId = next.Id, episode = next.Episode, title = next.EpisodeTitle };
            }
        }

        return new
        {
            reachable = true,
            fileName = status.FileName,
            state = status.State,
            isPlaying = status.IsPlaying,
            isPaused = status.IsPaused,
            positionMs = status.Position,
            durationMs = status.Duration,
            volume = status.Volume,
            muted = status.Muted,
            titleId,
            title = titleName,
            isAnime,
            season,
            episode,
            episodeTitle,
            episodeCount,
            year,
            type,
            prevEpisode,
            nextEpisode
        };
    }

    /// <summary>
    /// EnrichStatus variant that uses explicitly-provided scoped services (for WebSocket loop).
    /// </summary>
    private async Task<object> EnrichStatusWithScopeAsync(MpcStatus status, AppDbContext db, IMediaItemRepository mediaItemRepo, TmdbClient tmdbClient)
    {
        if (!status.Reachable)
        {
            return new
            {
                reachable = false, fileName = (string?)null, state = 0, isPlaying = false, isPaused = false,
                positionMs = 0L, durationMs = 0L, volume = 0, muted = false, titleId = (string?)null,
                title = (string?)null, isAnime = false, season = (int?)null, episode = (int?)null,
                episodeTitle = (string?)null, episodeCount = (int?)null, year = (int?)null,
                type = (string?)null, prevEpisode = (object?)null, nextEpisode = (object?)null
            };
        }

        Data.Entities.MediaItem? currentItem = null;
        if (!string.IsNullOrEmpty(status.FilePath))
        {
            currentItem = await db.MediaItems.Include(m => m.Title)
                .FirstOrDefaultAsync(m => m.FilePath == status.FilePath);
        }

        string? titleId = null, titleName = null, episodeTitleStr = null, type = null;
        int? season = null, ep = null, episodeCount = null, year = null;
        bool isAnime = false;
        object? prevEpisode = null, nextEpisode = null;

        if (currentItem?.Title != null)
        {
            titleId = currentItem.Title.Id;
            titleName = currentItem.Title.Name;
            type = currentItem.Title.Type;
            year = currentItem.Title.Year;
            isAnime = currentItem.Title.IsAnime;
            season = currentItem.Season;
            ep = currentItem.Episode;
            episodeTitleStr = currentItem.EpisodeTitle;

            if (season.HasValue && currentItem.Title.TmdbId.HasValue)
            {
                var cacheKey = $"epcount:{currentItem.Title.TmdbId}:{season}";
                if (!_cache.TryGetValue(cacheKey, out int cachedCount))
                {
                    cachedCount = await tmdbClient.GetEpisodeCountAsync(currentItem.Title.TmdbId.Value, season.Value);
                    _cache.Set(cacheKey, cachedCount, TimeSpan.FromHours(1));
                }
                episodeCount = cachedCount;
            }

            if (season.HasValue && ep.HasValue)
            {
                var prev = await mediaItemRepo.FindAdjacentEpisodeAsync(currentItem.TitleId, season.Value, ep.Value, next: false);
                var next = await mediaItemRepo.FindAdjacentEpisodeAsync(currentItem.TitleId, season.Value, ep.Value, next: true);
                if (prev != null) prevEpisode = new { mediaItemId = prev.Id, episode = prev.Episode, title = prev.EpisodeTitle };
                if (next != null) nextEpisode = new { mediaItemId = next.Id, episode = next.Episode, title = next.EpisodeTitle };
            }
        }

        return new
        {
            reachable = true, fileName = status.FileName, state = status.State,
            isPlaying = status.IsPlaying, isPaused = status.IsPaused,
            positionMs = status.Position, durationMs = status.Duration,
            volume = status.Volume, muted = status.Muted,
            titleId, title = titleName, isAnime, season, episode = ep,
            episodeTitle = episodeTitleStr, episodeCount, year, type, prevEpisode, nextEpisode
        };
    }

    private static int? ResolveCommand(string command)
    {
        return command.ToUpperInvariant() switch
        {
            "PLAYPAUSE" or "PLAY_PAUSE" => MpcCommands.PlayPause,
            "PLAY" => MpcCommands.Play,
            "PAUSE" => MpcCommands.Pause,
            "STOP" => MpcCommands.Stop,
            "SEEK" => MpcCommands.Seek,
            "MUTE" => MpcCommands.Mute,
            "VOLUME_UP" or "VOLUMEUP" => MpcCommands.VolumeUp,
            "VOLUME_DOWN" or "VOLUMEDOWN" => MpcCommands.VolumeDown,
            "VOLUME" => MpcCommands.VolumeUp, // Absolute volume not supported by MPC-BE; use volume up/down or mute
            _ => null
        };
    }
}

public record MpcCommandRequest(string Command, long? PositionMs = null);
public record MpcOpenRequest(string MediaItemId, List<string>? Playlist = null);
