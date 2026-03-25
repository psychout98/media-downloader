using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace MediaDownloader.Api.Controllers;

[ApiController]
public class SettingsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly RealDebridClient _rdClient;

    public SettingsController(IConfiguration configuration, RealDebridClient rdClient, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _rdClient = rdClient;
        _serviceProvider = serviceProvider;
    }

    private readonly IServiceProvider _serviceProvider;

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

        // Read existing .env preserving original structure
        var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        var originalLines = System.IO.File.Exists(envPath)
            ? System.IO.File.ReadAllLines(envPath).ToList()
            : new List<string>();

        var updatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Update values
        foreach (var (key, rawValue) in settings)
        {
            var value = Configuration.ConfigurationExtensions.StripQuotes(rawValue);
            written.Add(key);
            updatedKeys.Add(key);

            // Find and update existing line in-place
            var found = false;
            for (var i = 0; i < originalLines.Count; i++)
            {
                var trimmed = originalLines[i].Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx > 0 && trimmed[..eqIdx].Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    originalLines[i] = $"{key}={value}";
                    found = true;
                    break;
                }
            }

            // Append new keys at the end
            if (!found)
                originalLines.Add($"{key}={value}");
        }

        // Write back preserving comments and structure
        System.IO.File.WriteAllLines(envPath, originalLines);

        // Update in-process environment variables and reload configuration
        foreach (var (key, rawValue) in settings)
        {
            var value = Configuration.ConfigurationExtensions.StripQuotes(rawValue);
            Environment.SetEnvironmentVariable(key, value);
        }
        if (_configuration is IConfigurationRoot configRoot)
        {
            configRoot.Reload();
        }

        return Ok(new { ok = true, written });
    }

    [HttpGet("/api/settings/test-rd")]
    public async Task<IActionResult> TestRealDebrid()
    {
        var (ok, keySuffix) = await _rdClient.TestApiKeyAsync();
        return Ok(new { ok, keySuffix });
    }
}
