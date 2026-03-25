using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace MediaDownloader.Api.Controllers;

[ApiController]
public class SettingsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly RealDebridClient _rdClient;

    public SettingsController(IConfiguration configuration, RealDebridClient rdClient)
    {
        _configuration = configuration;
        _rdClient = rdClient;
    }

    [HttpGet("/api/settings")]
    public IActionResult GetSettings()
    {
        var result = new Dictionary<string, string?>();
        foreach (var key in Configuration.ConfigurationExtensions.KnownSettingKeys.Keys)
        {
            result[key] = _configuration[key];
        }
        return Ok(result);
    }

    [HttpPost("/api/settings")]
    public IActionResult UpdateSettings([FromBody] Dictionary<string, string> settings)
    {
        var written = new List<string>();

        foreach (var (key, rawValue) in settings)
        {
            if (!Configuration.ConfigurationExtensions.KnownSettingKeys.ContainsKey(key))
                return BadRequest(new { error = "validation_error", detail = $"Unknown setting key: {key}" });
        }

        // Read existing .env
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        var lines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (System.IO.File.Exists(envPath))
        {
            foreach (var line in System.IO.File.ReadAllLines(envPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0)
                    lines[trimmed[..eqIdx]] = trimmed[(eqIdx + 1)..];
            }
        }

        // Update values
        foreach (var (key, rawValue) in settings)
        {
            var value = Configuration.ConfigurationExtensions.StripQuotes(rawValue);
            lines[key] = value;
            written.Add(key);
        }

        // Write back
        var output = lines.Select(kvp => $"{kvp.Key}={kvp.Value}");
        System.IO.File.WriteAllLines(envPath, output);

        return Ok(new { ok = true, written });
    }

    [HttpGet("/api/settings/test-rd")]
    public async Task<IActionResult> TestRealDebrid()
    {
        var (ok, keySuffix) = await _rdClient.TestApiKeyAsync();
        return Ok(new { ok, keySuffix });
    }
}
