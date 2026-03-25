using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace MediaDownloader.Wpf.Services;

public class ApiClient : IDisposable
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ApiClient(int port = 8000)
    {
        _client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(10) };
    }

    public void UpdatePort(int port)
    {
        _client.BaseAddress = new Uri($"http://localhost:{port}");
    }

    public async Task<T?> GetAsync<T>(string path)
    {
        var response = await _client.GetAsync($"/api/{path}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    public async Task<T?> PostAsync<T>(string path, object? body = null)
    {
        var response = await _client.PostAsJsonAsync($"/api/{path}", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
    }

    public async Task<bool> IsReachableAsync()
    {
        try
        {
            await _client.GetAsync("/api/status");
            return true;
        }
        catch { return false; }
    }

    // DTOs
    public record StatusResponse(string Status, string MoviesDir, string TvDir, string ArchiveDir);
    public record VersionResponse(string Version, bool UpdateAvailable, string? LatestVersion, string? ReleaseUrl);
    public record JobResponse(string Id, string TitleId, string? Query, string Status, double Progress, long SizeBytes, long DownloadedBytes, string? TorrentName, string? Error, string CreatedAt);
    public record JobsResponse(JobResponse[] Jobs);
    public record SettingsResponse : Dictionary<string, string?> { }
    public record LibraryResponse(object[] Items, int Count);

    public async Task<StatusResponse?> GetStatusAsync() => await GetAsync<StatusResponse>("status");
    public async Task<VersionResponse?> GetVersionAsync() => await GetAsync<VersionResponse>("version");
    public async Task<JobsResponse?> GetJobsAsync() => await GetAsync<JobsResponse>("jobs");
    public async Task<Dictionary<string, string?>?> GetSettingsAsync() => await GetAsync<Dictionary<string, string?>>("settings");
    public async Task PostSettingsAsync(Dictionary<string, string> settings) => await PostAsync<object>("settings", settings);
    public async Task<LibraryResponse?> GetLibraryAsync() => await GetAsync<LibraryResponse>("library");
    public async Task PostRefreshLibraryAsync() => await PostAsync<object>("library/refresh");

    public void Dispose() => _client.Dispose();
}
