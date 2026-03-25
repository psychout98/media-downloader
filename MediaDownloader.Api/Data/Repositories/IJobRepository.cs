using MediaDownloader.Api.Data.Entities;

namespace MediaDownloader.Api.Data.Repositories;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(string id);
    Task<List<Job>> GetAllAsync(int limit = 200);
    Task<List<Job>> GetPendingAsync();
    Task<Job> CreateAsync(Job job);
    Task UpdateAsync(Job job);
    Task<bool> DeleteAsync(string id);
    Task AppendLogAsync(string id, string line);
}
