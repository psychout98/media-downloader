using MediaDownloader.Api.Configuration;
using MediaDownloader.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Controllers;

[ApiController]
public class SystemController : ControllerBase
{
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly UpdateService _updateService;

    public SystemController(IOptionsMonitor<AppSettings> settings, UpdateService updateService)
    {
        _settings = settings;
        _updateService = updateService;
    }

    [HttpGet("/api/status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            status = "ok",
            moviesDir = _settings.CurrentValue.Media.MoviesDir,
            tvDir = _settings.CurrentValue.Media.TvDir,
            archiveDir = _settings.CurrentValue.Media.ArchiveDir
        });
    }

    [HttpGet("/api/logs")]
    public IActionResult GetLogs([FromQuery] int lines = 200)
    {
        var logDir = Path.Combine(_settings.CurrentValue.Media.AppDataDir, "logs");
        if (!Directory.Exists(logDir))
            return Ok(new { lines = Array.Empty<string>() });

        var logFiles = Directory.GetFiles(logDir, "*.log").OrderByDescending(f => f).ToArray();
        if (logFiles.Length == 0)
            return Ok(new { lines = Array.Empty<string>() });

        var allLines = System.IO.File.ReadAllLines(logFiles[0]);
        var tailLines = allLines.TakeLast(lines).ToArray();

        return Ok(new { lines = tailLines });
    }

    [HttpGet("/api/version")]
    public async Task<IActionResult> GetVersion()
    {
        var info = await _updateService.CheckForUpdatesAsync();
        return Ok(new
        {
            version = info.Version,
            updateAvailable = info.UpdateAvailable,
            latestVersion = info.LatestVersion,
            releaseUrl = info.ReleaseUrl
        });
    }
}
