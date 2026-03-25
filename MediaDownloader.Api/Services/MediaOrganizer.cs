using System.Text.RegularExpressions;
using MediaDownloader.Api.Clients;
using MediaDownloader.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Services;

public class MediaOrganizer
{
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger<MediaOrganizer> _logger;

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mkv", ".mp4", ".avi" };

    public MediaOrganizer(IOptionsMonitor<AppSettings> settings, ILogger<MediaOrganizer> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Remove illegal filename chars, replace colon with " - ", collapse spaces, strip leading/trailing dots and spaces.
    /// </summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        // Remove <>"/\|?*
        name = Regex.Replace(name, @"[<>""/\\|?*]", "");
        // Replace colon with " - "
        name = name.Replace(":", " - ");
        // Collapse whitespace
        name = Regex.Replace(name, @"\s+", " ");
        // Strip leading/trailing dots and spaces
        name = name.Trim('.', ' ');

        return name;
    }

    /// <summary>
    /// Find the largest video file in a directory tree.
    /// </summary>
    public static string? PickVideoFile(string directory)
    {
        if (!Directory.Exists(directory)) return null;

        string? largest = null;
        long maxSize = 0;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(file);
            if (!VideoExtensions.Contains(ext)) continue;

            var info = new FileInfo(file);
            if (info.Length > maxSize)
            {
                maxSize = info.Length;
                largest = file;
            }
        }

        return largest;
    }

    /// <summary>
    /// Build the destination path for a media file.
    /// </summary>
    public string BuildDestination(MediaInfo media, string sourceFile, int? episode = null, string? episodeTitle = null)
    {
        var ext = Path.GetExtension(sourceFile);
        var sanitizedTitle = Sanitize(media.Title);
        var tmdbId = media.TmdbId;

        if (media.Type == "movie")
        {
            var folderName = $"{sanitizedTitle} [{tmdbId}]";
            var fileName = media.Year.HasValue
                ? $"{sanitizedTitle} ({media.Year}){ext}"
                : $"{sanitizedTitle}{ext}";
            return Path.Combine(_settings.CurrentValue.Media.MoviesDir, folderName, fileName);
        }

        // TV show
        var tvDir = _settings.CurrentValue.Media.TvDir;
        var showFolder = $"{sanitizedTitle} [{tmdbId}]";
        var s = media.Season ?? 1;
        var ep = episode ?? media.Episode;

        if (ep.HasValue)
        {
            var epTitle = !string.IsNullOrEmpty(episodeTitle) ? Sanitize(episodeTitle) : null;
            var fileName = epTitle != null
                ? $"S{s:D2}E{ep:D2} - {epTitle}{ext}"
                : $"S{s:D2}E{ep:D2}{ext}";
            return Path.Combine(tvDir, showFolder, fileName);
        }

        // Season pack fallback — keep original filename
        var originalName = Path.GetFileName(sourceFile);
        return Path.Combine(tvDir, showFolder, originalName);
    }

    /// <summary>
    /// Move a file (or largest video from a directory) to its organized destination.
    /// </summary>
    public async Task<string> OrganizeAsync(string sourcePath, MediaInfo media, int? episode = null, string? episodeTitle = null)
    {
        string fileToMove;

        if (Directory.Exists(sourcePath))
        {
            var video = PickVideoFile(sourcePath);
            if (video == null)
                throw new FileNotFoundException($"No video files found in {sourcePath}");
            fileToMove = video;
        }
        else if (File.Exists(sourcePath))
        {
            fileToMove = sourcePath;
        }
        else
        {
            throw new FileNotFoundException($"Source not found: {sourcePath}");
        }

        var destination = BuildDestination(media, fileToMove, episode, episodeTitle);
        var destDir = Path.GetDirectoryName(destination)!;

        Directory.CreateDirectory(destDir);

        // Overwrite if exists
        if (File.Exists(destination))
            File.Delete(destination);

        File.Move(fileToMove, destination);
        _logger.LogInformation("Organized {Source} → {Destination}", fileToMove, destination);

        return await Task.FromResult(destination);
    }
}
