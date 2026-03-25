namespace MediaDownloader.Api.Configuration;

public static class ConfigurationExtensions
{
    public static IServiceCollection AddAppSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AppSettings>(options =>
        {
            var tmdbKey = configuration["TMDB_API_KEY"] ?? string.Empty;
            var rdKey = configuration["REAL_DEBRID_API_KEY"] ?? string.Empty;

            options.Tmdb = new TmdbSettings { ApiKey = tmdbKey };
            options.RealDebrid = new RealDebridSettings { ApiKey = rdKey };
            options.Media = new MediaSettings
            {
                MoviesDir = configuration["MOVIES_DIR"] ?? string.Empty,
                TvDir = configuration["TV_DIR"] ?? string.Empty,
                ArchiveDir = configuration["ARCHIVE_DIR"] ?? string.Empty,
                AppDataDir = configuration["APP_DATA_DIR"] ?? string.Empty,
                WatchThreshold = double.TryParse(configuration["WATCH_THRESHOLD"], out var wt) ? wt : 0.85,
                MaxConcurrentDownloads = int.TryParse(configuration["MAX_CONCURRENT_DOWNLOADS"], out var mcd) ? mcd : 2,
                RdPollInterval = int.TryParse(configuration["RD_POLL_INTERVAL"], out var rpi) ? rpi : 30
            };
            options.Mpc = new MpcSettings
            {
                Url = configuration["MPC_BE_URL"] ?? "http://127.0.0.1:13579",
                ExePath = configuration["MPC_BE_EXE"] ?? string.Empty
            };
            options.Server = new ServerSettings
            {
                Host = configuration["HOST"] ?? "0.0.0.0",
                Port = int.TryParse(configuration["PORT"], out var port) ? port : 8000,
                GithubRepo = configuration["GITHUB_REPO"] ?? "psychout98/media-downloader"
            };
        });

        return services;
    }

    /// <summary>
    /// Maps known .env keys to their settings paths.
    /// </summary>
    public static readonly Dictionary<string, string> KnownSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TMDB_API_KEY"] = "TMDB_API_KEY",
        ["REAL_DEBRID_API_KEY"] = "REAL_DEBRID_API_KEY",
        ["MOVIES_DIR"] = "MOVIES_DIR",
        ["TV_DIR"] = "TV_DIR",
        ["ARCHIVE_DIR"] = "ARCHIVE_DIR",
        ["APP_DATA_DIR"] = "APP_DATA_DIR",
        ["WATCH_THRESHOLD"] = "WATCH_THRESHOLD",
        ["MPC_BE_URL"] = "MPC_BE_URL",
        ["MPC_BE_EXE"] = "MPC_BE_EXE",
        ["HOST"] = "HOST",
        ["PORT"] = "PORT",
        ["MAX_CONCURRENT_DOWNLOADS"] = "MAX_CONCURRENT_DOWNLOADS",
        ["RD_POLL_INTERVAL"] = "RD_POLL_INTERVAL",
        ["GITHUB_REPO"] = "GITHUB_REPO"
    };

    public static string StripQuotes(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }
        return value;
    }
}
