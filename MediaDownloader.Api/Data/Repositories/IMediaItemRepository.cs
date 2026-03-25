using MediaDownloader.Api.Data.Entities;

namespace MediaDownloader.Api.Data.Repositories;

public interface IMediaItemRepository
{
    Task<MediaItem?> GetByIdAsync(string id);
    Task<List<MediaItem>> GetByTitleIdAsync(string titleId, bool includeArchived = false);
    Task<List<MediaItem>> GetByJobIdAsync(string jobId);
    Task<MediaItem?> GetByTitleAndEpisodeAsync(string titleId, int season, int episode);
    Task<MediaItem?> FindAdjacentEpisodeAsync(string titleId, int season, int episode, bool next);
    Task<MediaItem> CreateAsync(MediaItem item);
    Task UpdateAsync(MediaItem item);
}
