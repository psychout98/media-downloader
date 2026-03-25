using System.ComponentModel.DataAnnotations;

namespace MediaDownloader.Api.Configuration;

public class AppSettings
{
    public TmdbSettings Tmdb { get; set; } = new();
    public RealDebridSettings RealDebrid { get; set; } = new();
    public MediaSettings Media { get; set; } = new();
    public MpcSettings Mpc { get; set; } = new();
    public ServerSettings Server { get; set; } = new();
}

public class TmdbSettings
{
    [Required]
    public string ApiKey { get; set; } = string.Empty;
}

public class RealDebridSettings
{
    [Required]
    public string ApiKey { get; set; } = string.Empty;
}

public class MediaSettings
{
    [Required]
    public string MoviesDir { get; set; } = string.Empty;

    [Required]
    public string TvDir { get; set; } = string.Empty;

    [Required]
    public string ArchiveDir { get; set; } = string.Empty;

    [Required]
    public string AppDataDir { get; set; } = string.Empty;

    public double WatchThreshold { get; set; } = 0.85;
    public int MaxConcurrentDownloads { get; set; } = 2;
    public int RdPollInterval { get; set; } = 30;
}

public class MpcSettings
{
    public string Url { get; set; } = "http://127.0.0.1:13579";
    public string ExePath { get; set; } = string.Empty;
}

public class ServerSettings
{
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 8000;
    public string GithubRepo { get; set; } = "psychout98/media-downloader";
}
