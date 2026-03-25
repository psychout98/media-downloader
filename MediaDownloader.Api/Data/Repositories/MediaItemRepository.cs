using MediaDownloader.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDownloader.Api.Data.Repositories;

public class MediaItemRepository : IMediaItemRepository
{
    private readonly AppDbContext _db;

    public MediaItemRepository(AppDbContext db) => _db = db;

    public async Task<MediaItem?> GetByIdAsync(string id) =>
        await _db.MediaItems.Include(m => m.Title).Include(m => m.WatchProgress).FirstOrDefaultAsync(m => m.Id == id);

    public async Task<List<MediaItem>> GetByTitleIdAsync(string titleId, bool includeArchived = false)
    {
        var query = _db.MediaItems
            .Include(m => m.WatchProgress)
            .Where(m => m.TitleId == titleId);

        if (!includeArchived)
            query = query.Where(m => !m.IsArchived);

        return await query.OrderBy(m => m.Season).ThenBy(m => m.Episode).ToListAsync();
    }

    public async Task<List<MediaItem>> GetByJobIdAsync(string jobId) =>
        await _db.MediaItems.Where(m => m.JobId == jobId).ToListAsync();

    public async Task<MediaItem?> GetByTitleAndEpisodeAsync(string titleId, int season, int episode) =>
        await _db.MediaItems.FirstOrDefaultAsync(m => m.TitleId == titleId && m.Season == season && m.Episode == episode);

    public async Task<MediaItem?> FindAdjacentEpisodeAsync(string titleId, int season, int episode, bool next)
    {
        if (next)
        {
            return await _db.MediaItems
                .Where(m => m.TitleId == titleId && !m.IsArchived && m.FilePath != null)
                .Where(m => (m.Season == season && m.Episode > episode) || m.Season > season)
                .OrderBy(m => m.Season).ThenBy(m => m.Episode)
                .FirstOrDefaultAsync();
        }

        return await _db.MediaItems
            .Where(m => m.TitleId == titleId && !m.IsArchived && m.FilePath != null)
            .Where(m => (m.Season == season && m.Episode < episode) || m.Season < season)
            .OrderByDescending(m => m.Season).ThenByDescending(m => m.Episode)
            .FirstOrDefaultAsync();
    }

    public async Task<MediaItem> CreateAsync(MediaItem item)
    {
        _db.MediaItems.Add(item);
        await _db.SaveChangesAsync();
        return item;
    }

    public async Task UpdateAsync(MediaItem item)
    {
        item.UpdatedAt = DateTimeOffset.UtcNow.ToString("o");
        _db.MediaItems.Update(item);
        await _db.SaveChangesAsync();
    }
}
