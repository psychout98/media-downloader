using System.Text.RegularExpressions;
using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Configuration;
using MediaDownloader.Api.Data;
using MediaDownloader.Api.Data.Entities;
using MediaDownloader.Api.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Services;

public class LibraryManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger<LibraryManager> _logger;

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".avi", ".m4v", ".wmv", ".flv", ".mov" };

    private static readonly Regex QualityTagPattern = new(
        @"(720p|1080p|2160p|4K|BluRay|BRRip|WEBRip|WEB-DL|HDRip|DVDRip|x264|x265|HEVC|AAC|DTS|REMUX|PROPER|REPACK)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public LibraryManager(IServiceScopeFactory scopeFactory, IOptionsMonitor<AppSettings> settings, ILogger<LibraryManager> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Walk media directories and create DB entries for undiscovered media.
    /// </summary>
    public async Task IndexAsync()
    {
        var dirs = new[]
        {
            (_settings.CurrentValue.Media.MoviesDir, "movie"),
            (_settings.CurrentValue.Media.TvDir, "tv"),
            (_settings.CurrentValue.Media.ArchiveDir, (string?)null)
        };

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existingPaths = (await db.MediaItems.Where(m => m.FilePath != null).Select(m => m.FilePath!).ToListAsync()).ToHashSet();

        foreach (var (dir, defaultType) in dirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (!VideoExtensions.Contains(Path.GetExtension(file))) continue;
                if (existingPaths.Contains(file)) continue;

                await IndexFileAsync(db, file, dir, defaultType);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task IndexFileAsync(AppDbContext db, string filePath, string rootDir, string? defaultType)
    {
        // Determine title folder (first dir under root)
        var relativePath = Path.GetRelativePath(rootDir, filePath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        var folderName = parts.Length > 1 ? parts[0] : Path.GetFileNameWithoutExtension(filePath);

        // Try to extract TMDB ID from folder name like "Title [12345]"
        int? tmdbId = null;
        var bracketMatch = Regex.Match(folderName, @"\[(\d+)\]");
        if (bracketMatch.Success)
            tmdbId = int.Parse(bracketMatch.Groups[1].Value);

        // Check if title already exists
        Title? title = null;
        if (tmdbId.HasValue)
            title = await db.Titles.FirstOrDefaultAsync(t => t.TmdbId == tmdbId);

        title ??= await db.Titles.FirstOrDefaultAsync(t => t.FolderName == folderName);

        if (title == null)
        {
            var (cleanedTitle, year) = ExtractTitleYear(folderName);
            var type = defaultType ?? GuessType(rootDir, filePath);

            title = new Title
            {
                Name = cleanedTitle,
                Year = year,
                TmdbId = tmdbId,
                Type = type,
                FolderName = folderName
            };
            db.Titles.Add(title);
        }

        // Parse season/episode from filename
        int? season = null, episode = null;
        string? episodeTitle = null;
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var seMatch = Regex.Match(fileName, @"S(\d{1,2})E(\d{1,3})", RegexOptions.IgnoreCase);
        if (seMatch.Success)
        {
            season = int.Parse(seMatch.Groups[1].Value);
            episode = int.Parse(seMatch.Groups[2].Value);

            // Try to extract episode title after "S01E03 - "
            var afterEp = fileName[(seMatch.Index + seMatch.Length)..].TrimStart(' ', '-', ' ');
            if (!string.IsNullOrEmpty(afterEp))
                episodeTitle = afterEp;
        }

        var isArchived = filePath.StartsWith(_settings.CurrentValue.Media.ArchiveDir, StringComparison.OrdinalIgnoreCase);

        db.MediaItems.Add(new MediaItem
        {
            TitleId = title.Id,
            Season = season,
            Episode = episode,
            EpisodeTitle = episodeTitle,
            FilePath = filePath,
            IsArchived = isArchived
        });
    }

    /// <summary>
    /// Parse "(2024)", ".2024.", or "- 2024" from folder name; remove quality tags.
    /// </summary>
    public static (string title, int? year) ExtractTitleYear(string folderName)
    {
        var name = folderName;

        // Remove bracket content like [12345]
        name = Regex.Replace(name, @"\[\d+\]", "").Trim();

        // Try to extract year
        int? year = null;
        var yearMatch = Regex.Match(name, @"[\.\s\-\(]*((?:19|20)\d{2})[\.\)\s]*$");
        if (yearMatch.Success)
        {
            year = int.Parse(yearMatch.Groups[1].Value);
            name = name[..yearMatch.Index];
        }

        name = CleanTitle(name);
        return (name, year);
    }

    /// <summary>
    /// Remove quality tags, replace dots with spaces, remove brackets, collapse spaces.
    /// </summary>
    public static string CleanTitle(string title)
    {
        var name = title;

        // Remove quality tags
        name = QualityTagPattern.Replace(name, "");
        // Replace dots with spaces
        name = name.Replace('.', ' ');
        // Remove bracket content
        name = Regex.Replace(name, @"\([^)]*\)", "");
        name = Regex.Replace(name, @"\[[^\]]*\]", "");
        // Collapse spaces
        name = Regex.Replace(name, @"\s+", " ");
        // Strip leading/trailing dots, dashes, spaces
        name = name.Trim('.', '-', ' ');

        return name;
    }

    /// <summary>
    /// Strip Windows-illegal characters, replace colon with dash.
    /// </summary>
    public static string SafeFolderName(string name)
    {
        name = Regex.Replace(name, @"[<>|?]", "");
        name = name.Replace(":", " -");
        return name.Trim();
    }

    /// <summary>
    /// Re-index filesystem, resolve titles via TMDB, rename folders, fetch posters.
    /// </summary>
    public async Task<RefreshResult> RefreshAsync()
    {
        var result = new RefreshResult();

        // Re-index to pick up new files
        await IndexAsync();

        using var scope = _scopeFactory.CreateScope();
        var titleRepo = scope.ServiceProvider.GetRequiredService<ITitleRepository>();
        var tmdbClient = scope.ServiceProvider.GetRequiredService<TmdbClient>();
        var titles = await titleRepo.GetAllAsync();

        foreach (var title in titles)
        {
            try
            {
                // Resolve via TMDB if missing tmdb_id
                if (!title.TmdbId.HasValue && !string.IsNullOrEmpty(title.Name))
                {
                    var (resolvedTitle, year, posterPath) = await tmdbClient.FuzzyResolveAsync(title.Name, title.Type);
                    title.Name = resolvedTitle;
                    if (year.HasValue) title.Year = year;
                    if (posterPath != null) title.PosterPath = posterPath;
                    await titleRepo.UpdateAsync(title);
                }

                // Fetch poster if missing
                if (title.PosterPath != null && title.TmdbId.HasValue)
                {
                    var posterDir = Path.Combine(_settings.CurrentValue.Media.AppDataDir, "posters");
                    var posterFile = Path.Combine(posterDir, $"{title.Id}.jpg");
                    if (!File.Exists(posterFile))
                    {
                        await SavePosterAsync(title.PosterPath, posterFile);
                        result.PostersFetched++;
                    }
                }

                result.TotalItems++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh title {Title}", title.Name);
                result.Errors.Add($"{title.Name}: {ex.Message}");
            }
        }

        return result;
    }

    private async Task SavePosterAsync(string posterPath, string destFile)
    {
        try
        {
            var url = $"https://image.tmdb.org/t/p/w500{posterPath}";
            using var httpClient = new HttpClient();
            var data = await httpClient.GetByteArrayAsync(url);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
            await File.WriteAllBytesAsync(destFile, data);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch poster {PosterPath}", posterPath);
        }
    }

    private string GuessType(string rootDir, string filePath)
    {
        if (rootDir == _settings.CurrentValue.Media.TvDir) return "tv";
        if (rootDir == _settings.CurrentValue.Media.MoviesDir) return "movie";

        // For archive dir, check if it has season/episode in filename
        var fileName = Path.GetFileName(filePath);
        return Regex.IsMatch(fileName, @"S\d{1,2}E\d{1,3}", RegexOptions.IgnoreCase) ? "tv" : "movie";
    }
}

public class RefreshResult
{
    public int Renamed { get; set; }
    public int PostersFetched { get; set; }
    public int TotalItems { get; set; }
    public List<string> Errors { get; set; } = new();
}
