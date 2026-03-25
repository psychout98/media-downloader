using MediaDownloader.Api.Data.Entities;

namespace MediaDownloader.Api.Data.Repositories;

public interface IProgressRepository
{
    Task<WatchProgress?> GetAsync(string mediaItemId);
    Task SaveAsync(string mediaItemId, long positionMs, long durationMs);
}
