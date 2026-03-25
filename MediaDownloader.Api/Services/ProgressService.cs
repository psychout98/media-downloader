using MediaDownloader.Api.Data.Repositories;

namespace MediaDownloader.Api.Services;

public class ProgressService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ProgressService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<WatchProgressDto?> GetProgressAsync(string mediaItemId)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProgressRepository>();
        var progress = await repo.GetAsync(mediaItemId);
        if (progress == null) return null;

        return new WatchProgressDto(progress.PositionMs, progress.DurationMs, progress.Watched);
    }

    public async Task SaveProgressAsync(string mediaItemId, long positionMs, long durationMs)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProgressRepository>();
        await repo.SaveAsync(mediaItemId, positionMs, durationMs);
    }
}

public record WatchProgressDto(long PositionMs, long DurationMs, bool Watched);
