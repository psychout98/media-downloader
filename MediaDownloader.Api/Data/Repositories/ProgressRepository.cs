using MediaDownloader.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDownloader.Api.Data.Repositories;

public class ProgressRepository : IProgressRepository
{
    private readonly AppDbContext _db;

    public ProgressRepository(AppDbContext db) => _db = db;

    public async Task<WatchProgress?> GetAsync(string mediaItemId) =>
        await _db.WatchProgress.FindAsync(mediaItemId);

    public async Task SaveAsync(string mediaItemId, long positionMs, long durationMs)
    {
        var existing = await _db.WatchProgress.FindAsync(mediaItemId);
        // Once watched, never reset back to false
        var watched = (existing?.Watched ?? false) || (durationMs > 0 && (double)positionMs / durationMs >= 0.85);

        if (existing != null)
        {
            existing.PositionMs = positionMs;
            existing.DurationMs = durationMs;
            existing.Watched = watched;
            existing.UpdatedAt = DateTimeOffset.UtcNow.ToString("o");
        }
        else
        {
            _db.WatchProgress.Add(new WatchProgress
            {
                MediaItemId = mediaItemId,
                PositionMs = positionMs,
                DurationMs = durationMs,
                Watched = watched,
                UpdatedAt = DateTimeOffset.UtcNow.ToString("o")
            });
        }

        await _db.SaveChangesAsync();
    }
}
