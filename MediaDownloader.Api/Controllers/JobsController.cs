using System.Text.Json;
using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Data.Entities;
using MediaDownloader.Api.Data.Repositories;
using MediaDownloader.Api.Services;
using MediaDownloader.Shared.Enums;
using MediaDownloader.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace MediaDownloader.Api.Controllers;

[ApiController]
public class JobsController : ControllerBase
{
    private readonly TmdbClient _tmdbClient;
    private readonly TorrentioClient _torrentioClient;
    private readonly ITitleRepository _titleRepo;
    private readonly IMediaItemRepository _mediaItemRepo;
    private readonly IJobRepository _jobRepo;
    private readonly JobProcessorService _jobProcessor;
    private readonly IMemoryCache _cache;

    public JobsController(
        TmdbClient tmdbClient,
        TorrentioClient torrentioClient,
        ITitleRepository titleRepo,
        IMediaItemRepository mediaItemRepo,
        IJobRepository jobRepo,
        JobProcessorService jobProcessor,
        IMemoryCache cache)
    {
        _tmdbClient = tmdbClient;
        _torrentioClient = torrentioClient;
        _titleRepo = titleRepo;
        _mediaItemRepo = mediaItemRepo;
        _jobRepo = jobRepo;
        _jobProcessor = jobProcessor;
        _cache = cache;
    }

    [HttpPost("/api/search")]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return UnprocessableEntity(new { error = "validation_error", detail = "Query cannot be empty" });

        var media = await _tmdbClient.SearchAsync(request.Query);
        var streams = await _torrentioClient.GetStreamsAsync(media.ImdbId, media.Type, media.Season, media.Episode);

        var searchId = Guid.NewGuid().ToString();

        // Cache with 15-minute sliding expiration
        var cacheEntry = new SearchCacheEntry(media, streams);
        _cache.Set(searchId, cacheEntry, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(15)
        });

        string? warning = null;
        if (streams.Count == 0)
            warning = "No streams found for this title";

        return Ok(new
        {
            searchId,
            media = new
            {
                tmdbId = media.TmdbId,
                title = media.Title,
                year = media.Year,
                type = media.Type,
                isAnime = media.IsAnime,
                imdbId = media.ImdbId,
                posterUrl = media.PosterUrl,
                season = media.Season,
                episode = media.Episode,
                overview = media.Overview
            },
            streams = streams.Select(s => new
            {
                index = s.Index,
                name = s.Name,
                infoHash = s.InfoHash,
                sizeBytes = s.SizeBytes,
                isCachedRd = s.IsCachedRd,
                seeders = s.Seeders
            }),
            warning
        });
    }

    [HttpPost("/api/download")]
    public async Task<IActionResult> Download([FromBody] DownloadRequest request)
    {
        if (!_cache.TryGetValue(request.SearchId, out SearchCacheEntry? cached) || cached == null)
            return NotFound(new { error = "not_found", detail = "Search expired or not found" });

        if (request.StreamIndex < 0 || request.StreamIndex >= cached.Streams.Count)
            return UnprocessableEntity(new { error = "validation_error", detail = "Stream index out of range" });

        var media = cached.Media;
        var stream = cached.Streams[request.StreamIndex];

        // Create or reuse title
        var title = media.TmdbId > 0
            ? await _titleRepo.GetByTmdbIdAsync(media.TmdbId)
            : null;

        if (title == null)
        {
            title = new Title
            {
                TmdbId = media.TmdbId > 0 ? media.TmdbId : null,
                ImdbId = media.ImdbId,
                Name = media.Title,
                Year = media.Year,
                Type = media.Type,
                IsAnime = media.IsAnime,
                Overview = media.Overview,
                PosterPath = media.PosterPath,
                FolderName = $"{MediaOrganizer.Sanitize(media.Title)} [{media.TmdbId}]"
            };
            await _titleRepo.CreateAsync(title);
        }

        // Create media item(s) with file_path = null
        var mediaItem = new MediaItem
        {
            TitleId = title.Id,
            Season = media.Season,
            Episode = media.Episode
        };

        // Create job
        var streamData = new StreamData
        {
            Media = new MediaInfoData
            {
                TmdbId = media.TmdbId,
                Title = media.Title,
                Year = media.Year,
                Type = media.Type,
                IsAnime = media.IsAnime,
                ImdbId = media.ImdbId,
                PosterPath = media.PosterPath,
                Season = media.Season,
                Episode = media.Episode,
                Overview = media.Overview
            },
            Stream = new StreamInfoData
            {
                Index = stream.Index,
                Name = stream.Name,
                InfoHash = stream.InfoHash,
                SizeBytes = stream.SizeBytes,
                IsCachedRd = stream.IsCachedRd,
                Seeders = stream.Seeders
            }
        };

        var job = new Job
        {
            TitleId = title.Id,
            Query = media.DisplayName,
            Season = media.Season,
            Episode = media.Episode,
            Status = JobStatus.Pending.ToApiString(),
            StreamData = JsonSerializer.Serialize(streamData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            })
        };
        await _jobRepo.CreateAsync(job);

        // Link media item to job
        mediaItem.JobId = job.Id;
        await _mediaItemRepo.CreateAsync(mediaItem);

        return StatusCode(201, new
        {
            jobId = job.Id,
            titleId = title.Id,
            status = "pending"
        });
    }

    [HttpGet("/api/jobs")]
    public async Task<IActionResult> GetJobs()
    {
        var jobs = await _jobRepo.GetAllAsync();
        return Ok(new { jobs = jobs.Select(MapJob) });
    }

    [HttpGet("/api/jobs/{id}")]
    public async Task<IActionResult> GetJob(string id)
    {
        var job = await _jobRepo.GetByIdAsync(id);
        if (job == null)
            return NotFound(new { error = "not_found", detail = $"No job found with ID {id}" });

        return Ok(MapJob(job));
    }

    [HttpDelete("/api/jobs/{id}")]
    public async Task<IActionResult> DeleteJob(string id)
    {
        var job = await _jobRepo.GetByIdAsync(id);
        if (job == null)
            return NotFound(new { error = "not_found", detail = $"No job found with ID {id}" });

        if (job.JobStatus.IsActive())
        {
            // Cancel active job
            _jobProcessor.CancelJob(id);
            job.JobStatus = JobStatus.Cancelled;
            await _jobRepo.UpdateAsync(job);
            return Ok(new { ok = true, status = "cancelled" });
        }

        // Delete completed/failed/cancelled job
        await _jobRepo.DeleteAsync(id);
        return Ok(new { ok = true, status = "deleted" });
    }

    [HttpPost("/api/jobs/{id}/retry")]
    public async Task<IActionResult> RetryJob(string id)
    {
        var job = await _jobRepo.GetByIdAsync(id);
        if (job == null)
            return NotFound(new { error = "not_found", detail = $"No job found with ID {id}" });

        if (!job.JobStatus.CanRetry())
            return BadRequest(new { error = "bad_request", detail = $"Cannot retry job with status {job.Status}" });

        job.JobStatus = JobStatus.Pending;
        job.Progress = 0;
        job.DownloadedBytes = 0;
        job.Error = null;
        await _jobRepo.UpdateAsync(job);

        return Ok(new { ok = true, status = "pending" });
    }

    private static object MapJob(Job job)
    {
        return new
        {
            id = job.Id,
            titleId = job.TitleId,
            query = job.Query,
            season = job.Season,
            episode = job.Episode,
            status = job.Status,
            progress = job.Progress,
            sizeBytes = job.SizeBytes,
            downloadedBytes = job.DownloadedBytes,
            quality = job.Quality,
            torrentName = job.TorrentName,
            error = job.Error,
            log = job.Log,
            createdAt = job.CreatedAt,
            updatedAt = job.UpdatedAt,
            title = job.Title != null ? new
            {
                id = job.Title.Id,
                tmdbId = job.Title.TmdbId,
                imdbId = job.Title.ImdbId,
                title = job.Title.Name,
                year = job.Title.Year,
                type = job.Title.Type,
                isAnime = job.Title.IsAnime,
                posterPath = job.Title.PosterPath
            } : null
        };
    }
}

public record SearchRequest(string Query);
public record DownloadRequest(string SearchId, int StreamIndex);

internal record SearchCacheEntry(MediaInfo Media, List<StreamInfo> Streams);
