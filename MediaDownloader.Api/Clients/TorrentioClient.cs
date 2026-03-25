using System.Text.Json;
using System.Text.RegularExpressions;
using MediaDownloader.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Clients;

public record StreamInfo(
    int Index,
    string Name,
    string? InfoHash,
    long SizeBytes,
    bool IsCachedRd,
    int Seeders);

public class TorrentioClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger<TorrentioClient> _logger;

    public TorrentioClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<AppSettings> settings, ILogger<TorrentioClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public string BuildUrl(string? imdbId, string type, int? season, int? episode)
    {
        var rdKey = _settings.CurrentValue.RealDebrid.ApiKey;
        var options = string.IsNullOrEmpty(rdKey)
            ? "sort=qualitysize|limit=20"
            : $"realdebrid={rdKey}|sort=qualitysize|limit=20";

        if (type == "movie")
            return $"{options}/stream/movie/{imdbId}.json";

        var ep = episode ?? 1;
        var s = season ?? 1;
        return $"{options}/stream/series/{imdbId}:{s}:{ep}.json";
    }

    public async Task<List<StreamInfo>> GetStreamsAsync(string? imdbId, string type, int? season, int? episode)
    {
        if (string.IsNullOrEmpty(imdbId))
            return new List<StreamInfo>();

        try
        {
            var client = _httpClientFactory.CreateClient("torrentio");
            var path = BuildUrl(imdbId, type, season, episode);
            var response = await client.GetAsync(path);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (!json.TryGetProperty("streams", out var streams) || streams.GetArrayLength() == 0)
                return new List<StreamInfo>();

            var result = new List<StreamInfo>();
            var index = 0;

            foreach (var stream in streams.EnumerateArray())
            {
                var name = stream.GetPropertyOrDefault("name") ?? "Unknown";
                var title = stream.GetPropertyOrDefault("title") ?? string.Empty;
                var infoHash = stream.GetPropertyOrDefault("infoHash");
                var isCached = stream.TryGetProperty("url", out _);
                var sizeBytes = ParseSize(title);
                var seeders = ParseSeeders(title);

                // Use the full "title" field as the display name (it has more info than "name")
                var displayName = !string.IsNullOrEmpty(title) ? $"{name}\n{title}" : name;

                result.Add(new StreamInfo(index, displayName, infoHash, sizeBytes, isCached, seeders));
                index++;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get streams from Torrentio for {ImdbId}", imdbId);
            return new List<StreamInfo>();
        }
    }

    public static long ParseSize(string title)
    {
        var match = Regex.Match(title, @"💾\s*([\d.]+)\s*(GB|MB|TB)", RegexOptions.IgnoreCase);
        if (!match.Success) return 0;

        var value = double.Parse(match.Groups[1].Value);
        var unit = match.Groups[2].Value.ToUpperInvariant();

        return unit switch
        {
            "TB" => (long)(value * 1_099_511_627_776),
            "GB" => (long)(value * 1_073_741_824),
            "MB" => (long)(value * 1_048_576),
            _ => 0
        };
    }

    public static int ParseSeeders(string title)
    {
        var match = Regex.Match(title, @"👤\s*(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }
}
