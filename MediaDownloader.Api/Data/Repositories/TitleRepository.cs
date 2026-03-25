using MediaDownloader.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDownloader.Api.Data.Repositories;

public class TitleRepository : ITitleRepository
{
    private readonly AppDbContext _db;

    public TitleRepository(AppDbContext db) => _db = db;

    public async Task<Title?> GetByIdAsync(string id) =>
        await _db.Titles.FindAsync(id);

    public async Task<Title?> GetByTmdbIdAsync(int tmdbId) =>
        await _db.Titles.FirstOrDefaultAsync(t => t.TmdbId == tmdbId);

    public async Task<List<Title>> GetAllAsync(string? type = null, string? search = null)
    {
        var query = _db.Titles.AsQueryable();

        if (!string.IsNullOrEmpty(type))
            query = query.Where(t => t.Type == type);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => EF.Functions.Like(t.Name, $"%{search}%"));

        return await query.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<Title> CreateAsync(Title title)
    {
        _db.Titles.Add(title);
        await _db.SaveChangesAsync();
        return title;
    }

    public async Task UpdateAsync(Title title)
    {
        title.UpdatedAt = DateTimeOffset.UtcNow.ToString("o");
        _db.Titles.Update(title);
        await _db.SaveChangesAsync();
    }

    public async Task<int> GetCountAsync(string? type = null)
    {
        var query = _db.Titles.AsQueryable();
        if (!string.IsNullOrEmpty(type))
            query = query.Where(t => t.Type == type);
        return await query.CountAsync();
    }
}
