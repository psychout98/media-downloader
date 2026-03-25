using MediaDownloader.Api.Data.Entities;

namespace MediaDownloader.Api.Data.Repositories;

public interface ITitleRepository
{
    Task<Title?> GetByIdAsync(string id);
    Task<Title?> GetByTmdbIdAsync(int tmdbId);
    Task<List<Title>> GetAllAsync(string? type = null, string? search = null);
    Task<Title> CreateAsync(Title title);
    Task UpdateAsync(Title title);
    Task<int> GetCountAsync(string? type = null);
}
