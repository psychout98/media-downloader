using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using MediaDownloader.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Clients;

public record MpcStatus(
    string? File,
    string? FilePath,
    string? FileName,
    int State,
    bool IsPlaying,
    bool IsPaused,
    long Position,
    long Duration,
    int Volume,
    bool Muted,
    bool Reachable);

public static class MpcCommands
{
    public const int PlayPause = 887;
    public const int Stop = 888;
    public const int Seek = 889;
    public const int Play = 891;
    public const int Pause = 892;
    public const int VolumeUp = 907;
    public const int VolumeDown = 908;
    public const int Mute = 909;
    public const int OpenFile = -1;
}

public class MpcClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger<MpcClient> _logger;

    public MpcClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<AppSettings> settings, ILogger<MpcClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    private string BaseUrl => _settings.CurrentValue.Mpc.Url.TrimEnd('/');

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("mpc");
        client.BaseAddress = new Uri(BaseUrl);
        return client;
    }

    public async Task<MpcStatus> GetStatusAsync()
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync("/variables.html");
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync();
            var vars = ParseVariables(body);

            var filePath = vars.GetValueOrDefault("filepath") ?? vars.GetValueOrDefault("filepatharg");
            if (filePath != null)
                filePath = HttpUtility.UrlDecode(filePath);

            var file = vars.GetValueOrDefault("file") ?? filePath;
            var fileName = vars.GetValueOrDefault("filename") ?? ExtractFileName(filePath);
            var state = int.TryParse(vars.GetValueOrDefault("state"), out var s) ? s : 0;
            var position = long.TryParse(vars.GetValueOrDefault("position"), out var p) ? p : 0;
            var duration = long.TryParse(vars.GetValueOrDefault("duration"), out var d) ? d : 0;
            var volume = int.TryParse(vars.GetValueOrDefault("volumelevel"), out var v) ? v : 100;
            var muted = vars.GetValueOrDefault("muted") == "1";

            return new MpcStatus(
                File: file,
                FilePath: filePath,
                FileName: fileName,
                State: state,
                IsPlaying: state == 2,
                IsPaused: state == 1,
                Position: position,
                Duration: duration,
                Volume: volume,
                Muted: muted,
                Reachable: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MPC-BE not reachable");
            return new MpcStatus(null, null, null, 0, false, false, 0, 0, 100, false, false);
        }
    }

    public async Task<bool> SendCommandAsync(int commandId, long? positionMs = null)
    {
        try
        {
            var client = CreateClient();
            var url = $"/command.html?wm_command={commandId}";
            if (positionMs.HasValue && commandId == MpcCommands.Seek)
                url += $"&position={positionMs.Value}";

            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send MPC command {Command}", commandId);
            return false;
        }
    }

    public async Task<bool> OpenFileAsync(string path)
    {
        try
        {
            var client = CreateClient();
            var url = $"/command.html?wm_command={MpcCommands.OpenFile}&path={HttpUtility.UrlEncode(path)}";
            var response = await client.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open file in MPC: {Path}", path);
            return false;
        }
    }

    public async Task<bool> PingAsync()
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync("/variables.html");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public static Dictionary<string, string> ParseVariables(string body)
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Try JSON format first
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            if (json.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in json.EnumerateObject())
                    vars[prop.Name] = prop.Value.ToString();
                return vars;
            }
        }
        catch { /* not JSON */ }

        // Try legacy OnVariable() JS format: OnVariable('key', 'value')
        var jsMatches = Regex.Matches(body, @"OnVariable\s*\(\s*'([^']+)'\s*,\s*'([^']*)'\s*\)");
        if (jsMatches.Count > 0)
        {
            foreach (Match match in jsMatches)
                vars[match.Groups[1].Value] = match.Groups[2].Value;
            return vars;
        }

        // Try HTML <p> format: <p id="key">value</p>
        var htmlMatches = Regex.Matches(body, @"<p\s+id=""([^""]+)"">([^<]*)</p>");
        foreach (Match match in htmlMatches)
            vars[match.Groups[1].Value] = match.Groups[2].Value;

        return vars;
    }

    public static string MsToString(long ms)
    {
        if (ms < 0) ms = 0;

        var totalSeconds = ms / 1000;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{minutes}:{seconds:D2}";
    }

    private static string? ExtractFileName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        // Handle both Windows and POSIX paths
        var lastSep = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return lastSep >= 0 ? path[(lastSep + 1)..] : path;
    }
}
