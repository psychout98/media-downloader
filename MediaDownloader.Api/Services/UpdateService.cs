using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;

namespace MediaDownloader.Api.Services;

public class UpdateService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _githubRepo;
    private readonly ILogger<UpdateService> _logger;

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.1";

    public UpdateService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<UpdateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _githubRepo = configuration["GITHUB_REPO"] ?? "psychout98/media-downloader";
        _logger = logger;
    }

    public async Task<VersionInfo> CheckForUpdatesAsync()
    {
        var result = new VersionInfo { Version = Version };

        try
        {
            var client = _httpClientFactory.CreateClient("github");
            var response = await client.GetFromJsonAsync<GitHubRelease>(
                $"repos/{_githubRepo}/releases/latest");

            if (response?.TagName != null)
            {
                var latest = response.TagName.TrimStart('v');
                result.LatestVersion = latest;
                result.ReleaseUrl = response.HtmlUrl;
                result.UpdateAvailable = IsNewer(latest, Version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates — GitHub API unreachable");
            result.UpdateAvailable = false;
        }

        return result;
    }

    private static bool IsNewer(string latest, string current)
    {
        if (System.Version.TryParse(latest, out var latestVersion) &&
            System.Version.TryParse(current, out var currentVersion))
        {
            return latestVersion > currentVersion;
        }
        return false;
    }

    public class VersionInfo
    {
        public string Version { get; set; } = string.Empty;
        public bool UpdateAvailable { get; set; }
        public string? LatestVersion { get; set; }
        public string? ReleaseUrl { get; set; }
    }

    private class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
