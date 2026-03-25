using System.Net.Http.Headers;
using System.Text.Json;
using MediaDownloader.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Clients;

public class RealDebridException : Exception
{
    public RealDebridException(string message) : base(message) { }
    public RealDebridException(string message, Exception inner) : base(message, inner) { }
}

public class RealDebridClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger<RealDebridClient> _logger;

    private static readonly HashSet<string> ErrorStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "error", "virus", "dead", "magnet_error" };

    public RealDebridClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<AppSettings> settings, ILogger<RealDebridClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("realdebrid");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.CurrentValue.RealDebrid.ApiKey);
        return client;
    }

    public async Task<bool> IsCachedAsync(string hash)
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync($"torrents/instantAvailability/{hash}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            // Check if hash key exists and has a non-empty rd array
            if (json.TryGetProperty(hash.ToLowerInvariant(), out var hashData) &&
                hashData.TryGetProperty("rd", out var rd) &&
                rd.GetArrayLength() > 0)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check cache status for {Hash}", hash);
        }

        return false;
    }

    public async Task<string> AddMagnetAsync(string magnetLink)
    {
        var client = CreateClient();
        var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("magnet", magnetLink) });
        var response = await client.PostAsync("torrents/addMagnet", content);

        if (!response.IsSuccessStatusCode)
            throw new RealDebridException($"Failed to add magnet: HTTP {(int)response.StatusCode}");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = json.GetPropertyOrDefault("id");
        if (string.IsNullOrEmpty(id))
            throw new RealDebridException("No torrent ID returned from addMagnet");

        return id;
    }

    public async Task SelectAllFilesAsync(string torrentId)
    {
        var client = CreateClient();
        var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("files", "all") });
        var response = await client.PostAsync($"torrents/selectFiles/{torrentId}", content);

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NoContent)
            throw new RealDebridException($"Failed to select files: HTTP {(int)response.StatusCode}");
    }

    public async Task<List<string>> WaitUntilDownloadedAsync(string torrentId, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var pollInterval = _settings.CurrentValue.Media.RdPollInterval;
        var timeout = TimeSpan.FromMinutes(30);
        var startTime = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow - startTime > timeout)
                throw new RealDebridException("Timed out waiting for Real-Debrid download");

            var client = CreateClient();
            var response = await client.GetAsync($"torrents/info/{torrentId}", ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var status = json.GetPropertyOrDefault("status") ?? string.Empty;

            if (json.TryGetProperty("progress", out var prog))
                progress?.Report((int)prog.GetDouble());

            if (status.Equals("downloaded", StringComparison.OrdinalIgnoreCase))
            {
                var links = new List<string>();
                if (json.TryGetProperty("links", out var linksArr))
                {
                    foreach (var link in linksArr.EnumerateArray())
                    {
                        var l = link.GetString();
                        if (l != null) links.Add(l);
                    }
                }

                if (links.Count == 0)
                    throw new RealDebridException("Download completed but no links returned");

                return links;
            }

            if (ErrorStatuses.Contains(status))
                throw new RealDebridException($"Real-Debrid download failed with status: {status}");

            await Task.Delay(TimeSpan.FromSeconds(pollInterval), ct);
        }

        throw new OperationCanceledException("Download was cancelled");
    }

    public async Task<(string Url, long FileSize)> UnrestrictLinkAsync(string link)
    {
        var client = CreateClient();
        var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("link", link) });
        var response = await client.PostAsync("unrestrict/link", content);

        if (!response.IsSuccessStatusCode)
            throw new RealDebridException($"Failed to unrestrict link: HTTP {(int)response.StatusCode}");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var url = json.GetPropertyOrDefault("download");
        if (string.IsNullOrEmpty(url))
            throw new RealDebridException("No download URL returned from unrestrict");

        long fileSize = 0;
        if (json.TryGetProperty("filesize", out var size))
            fileSize = size.GetInt64();

        return (url, fileSize);
    }

    public async Task<List<(string Url, long Size)>> UnrestrictAllAsync(List<string> links)
    {
        var results = new List<(string Url, long Size)>();
        foreach (var link in links)
        {
            var (url, size) = await UnrestrictLinkAsync(link);
            results.Add((url, size));
        }
        return results;
    }

    public async Task<List<(string Url, long Size)>> DownloadMagnetAsync(string magnetLink, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var torrentId = await AddMagnetAsync(magnetLink);
        await SelectAllFilesAsync(torrentId);
        var links = await WaitUntilDownloadedAsync(torrentId, progress, ct);
        return await UnrestrictAllAsync(links);
    }

    /// <summary>
    /// Validates the API key by making a simple request.
    /// </summary>
    public async Task<(bool Ok, string? KeySuffix)> TestApiKeyAsync()
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync("user");
            if (response.IsSuccessStatusCode)
            {
                var key = _settings.CurrentValue.RealDebrid.ApiKey;
                var suffix = key.Length >= 4 ? key[^4..] : key;
                return (true, suffix);
            }
        }
        catch { /* handled below */ }

        return (false, null);
    }
}
