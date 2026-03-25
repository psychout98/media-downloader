using MediaDownloader.Api.Data.Entities;
using MediaDownloader.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace MediaDownloader.Api.Data.Repositories;

public class JobRepository : IJobRepository
{
    private readonly AppDbContext _db;

    public JobRepository(AppDbContext db) => _db = db;

    public async Task<Job?> GetByIdAsync(string id) =>
        await _db.Jobs.Include(j => j.Title).FirstOrDefaultAsync(j => j.Id == id);

    public async Task<List<Job>> GetAllAsync(int limit = 200) =>
        await _db.Jobs.Include(j => j.Title)
            .OrderByDescending(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync();

    public async Task<List<Job>> GetPendingAsync() =>
        await _db.Jobs
            .Where(j => j.Status == JobStatus.Pending.ToApiString())
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();

    public async Task<Job> CreateAsync(Job job)
    {
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    public async Task UpdateAsync(Job job)
    {
        job.UpdatedAt = DateTimeOffset.UtcNow.ToString("o");

        var entry = _db.Entry(job);
        if (entry.State == EntityState.Detached)
            _db.Jobs.Attach(job);

        // Mark all properties as modified EXCEPT Log, which is managed via AppendLogAsync raw SQL
        entry.State = EntityState.Modified;
        entry.Property(j => j.Log).IsModified = false;

        await _db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job == null) return false;
        _db.Jobs.Remove(job);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task AppendLogAsync(string id, string line)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("HH:mm:ss");
        var logLine = $"[{timestamp}] {line}\n";
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE jobs SET log = COALESCE(log, '') || @p0 WHERE id = @p1",
            logLine, id);
    }
}
