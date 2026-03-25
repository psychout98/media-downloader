using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using MediaDownloader.Api.Configuration;
using Microsoft.Extensions.Options;

namespace MediaDownloader.Api.Clients;

public record MediaInfo(
    int TmdbId,
    string Title,
    int? Year,
    string Type,
    bool IsAnime,
    string? ImdbId,
    string? PosterPath,
    int? Season,
    int? Episode,
    string? Overview)
{
    public string? PosterUrl => PosterPath != null
        ? $"https://image.tmdb.org/t/p/w500{PosterPath}"
        : null;

    public string DisplayName
    {
        get
        {
            if (Type == "movie")
                return Year.HasValue ? $"{Title} ({Year})" : Title;
            if (Season.HasValue && Episode.HasValue)
                return $"{Title} S{Season:D2}E{Episode:D2}";
            if (Season.HasValue)
                return $"{Title} Season {Season}";
            return Title;
        }
    }
}

public class TmdbClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<AppSettings> _settings;
    private readonly ILogger<TmdbClient> _logger;

    private const int AnimeKeywordId = 210024;
    private const int AnimationGenreId = 16;

    public TmdbClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<AppSettings> settings, ILogger<TmdbClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("tmdb");
        return client;
    }

    private string ApiKey => _settings.CurrentValue.Tmdb.ApiKey;

    public static (string query, int? season, int? episode) ParseQuery(string rawQuery)
    {
        var query = rawQuery.Trim();
        int? season = null;
        int? episode = null;

        // Preserve IMDb URLs — don't strip year from them
        if (query.Contains("imdb.com", StringComparison.OrdinalIgnoreCase))
            return (query, season, episode);

        // Extract S01E03 pattern
        var seMatch = Regex.Match(query, @"S(\d{1,2})E(\d{1,3})", RegexOptions.IgnoreCase);
        if (seMatch.Success)
        {
            season = int.Parse(seMatch.Groups[1].Value);
            episode = int.Parse(seMatch.Groups[2].Value);
            query = query.Remove(seMatch.Index, seMatch.Length).Trim();
        }
        else
        {
            // Extract "Season X" pattern
            var seasonMatch = Regex.Match(query, @"Season\s+(\d{1,2})", RegexOptions.IgnoreCase);
            if (seasonMatch.Success)
            {
                season = int.Parse(seasonMatch.Groups[1].Value);
                query = query.Remove(seasonMatch.Index, seasonMatch.Length).Trim();
            }
            else
            {
                // Extract S03 pattern (season only)
                var sMatch = Regex.Match(query, @"S(\d{1,2})(?!\d|E)", RegexOptions.IgnoreCase);
                if (sMatch.Success)
                {
                    season = int.Parse(sMatch.Groups[1].Value);
                    query = query.Remove(sMatch.Index, sMatch.Length).Trim();
                }
            }

            // Extract "Episode X" pattern
            var episodeMatch = Regex.Match(query, @"Episode\s+(\d{1,3})", RegexOptions.IgnoreCase);
            if (episodeMatch.Success)
            {
                episode = int.Parse(episodeMatch.Groups[1].Value);
                query = query.Remove(episodeMatch.Index, episodeMatch.Length).Trim();
            }
        }

        // Strip trailing year: "Title 2010" or "Title (2010)"
        query = Regex.Replace(query, @"\s*\(?\d{4}\)?\s*$", "").Trim();

        return (query, season, episode);
    }

    public async Task<MediaInfo> SearchAsync(string rawQuery)
    {
        var (query, season, episode) = ParseQuery(rawQuery);

        // Handle IMDb URL
        if (query.Contains("imdb.com", StringComparison.OrdinalIgnoreCase))
        {
            var imdbMatch = Regex.Match(query, @"tt\d+");
            if (imdbMatch.Success)
            {
                var imdbId = imdbMatch.Value;
                var resolved = await ResolveImdbIdAsync(imdbId);
                if (resolved != null)
                    return resolved with { Season = season, Episode = episode };
            }
        }

        var client = CreateClient();
        var url = $"search/multi?api_key={ApiKey}&query={HttpUtility.UrlEncode(query)}";
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var results = json.GetProperty("results");

        if (results.GetArrayLength() == 0)
            throw new InvalidOperationException($"No results found for query: {rawQuery}");

        foreach (var result in results.EnumerateArray())
        {
            var mediaType = result.GetProperty("media_type").GetString();
            if (mediaType is not ("movie" or "tv")) continue;

            return await BuildMediaInfoAsync(result, mediaType, season, episode);
        }

        throw new InvalidOperationException($"No movie or TV results found for query: {rawQuery}");
    }

    private async Task<MediaInfo?> ResolveImdbIdAsync(string imdbId)
    {
        try
        {
            var client = CreateClient();
            var url = $"find/{imdbId}?api_key={ApiKey}&external_source=imdb_id";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            // Check movie results first, then TV
            foreach (var key in new[] { "movie_results", "tv_results" })
            {
                if (json.TryGetProperty(key, out var arr) && arr.GetArrayLength() > 0)
                {
                    var result = arr[0];
                    var mediaType = key == "movie_results" ? "movie" : "tv";
                    return await BuildMediaInfoAsync(result, mediaType, null, null);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve IMDb ID {ImdbId}", imdbId);
        }

        return null;
    }

    private async Task<MediaInfo> BuildMediaInfoAsync(JsonElement result, string type, int? season, int? episode)
    {
        var tmdbId = result.GetProperty("id").GetInt32();
        var title = type == "movie"
            ? result.GetPropertyOrDefault("title")
            : result.GetPropertyOrDefault("name");
        var year = type == "movie"
            ? ParseYear(result.GetPropertyOrDefault("release_date"))
            : ParseYear(result.GetPropertyOrDefault("first_air_date"));
        var posterPath = result.GetPropertyOrDefault("poster_path");
        var overview = result.GetPropertyOrDefault("overview");

        // Get IMDb ID
        string? imdbId = null;
        try
        {
            var client = CreateClient();
            var externalUrl = type == "movie"
                ? $"movie/{tmdbId}/external_ids?api_key={ApiKey}"
                : $"tv/{tmdbId}/external_ids?api_key={ApiKey}";
            var extResponse = await client.GetAsync(externalUrl);
            if (extResponse.IsSuccessStatusCode)
            {
                var extJson = await extResponse.Content.ReadFromJsonAsync<JsonElement>();
                imdbId = extJson.GetPropertyOrDefault("imdb_id");
            }
        }
        catch { /* non-critical */ }

        // Anime detection
        var isAnime = await DetectAnimeAsync(tmdbId, type, result);

        return new MediaInfo(tmdbId, title ?? "Unknown", year, type, isAnime, imdbId, posterPath, season, episode, overview);
    }

    private async Task<bool> DetectAnimeAsync(int tmdbId, string type, JsonElement result)
    {
        try
        {
            // Check keywords first
            var client = CreateClient();
            var keywordUrl = type == "movie"
                ? $"movie/{tmdbId}/keywords?api_key={ApiKey}"
                : $"tv/{tmdbId}/keywords?api_key={ApiKey}";
            var kwResponse = await client.GetAsync(keywordUrl);
            if (kwResponse.IsSuccessStatusCode)
            {
                var kwJson = await kwResponse.Content.ReadFromJsonAsync<JsonElement>();
                var keywordsProperty = type == "movie" ? "keywords" : "results";
                if (kwJson.TryGetProperty(keywordsProperty, out var keywords))
                {
                    foreach (var kw in keywords.EnumerateArray())
                    {
                        if (kw.GetProperty("id").GetInt32() == AnimeKeywordId)
                            return true;
                    }
                }
            }
        }
        catch { /* non-critical */ }

        // Fallback: Animation genre + Japanese origin
        if (result.TryGetProperty("genre_ids", out var genres))
        {
            var hasAnimation = false;
            foreach (var g in genres.EnumerateArray())
            {
                if (g.GetInt32() == AnimationGenreId)
                {
                    hasAnimation = true;
                    break;
                }
            }

            if (hasAnimation)
            {
                var lang = result.GetPropertyOrDefault("original_language");
                if (lang == "ja")
                    return true;
            }
        }

        return false;
    }

    public async Task<int> GetEpisodeCountAsync(int tmdbId, int season)
    {
        try
        {
            var client = CreateClient();
            var url = $"tv/{tmdbId}/season/{season}?api_key={ApiKey}";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            if (json.TryGetProperty("episodes", out var episodes))
                return episodes.GetArrayLength();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get episode count for TMDB {TmdbId} S{Season}", tmdbId, season);
        }

        return 0;
    }

    public async Task<string> GetEpisodeTitleAsync(int tmdbId, int season, int episode)
    {
        try
        {
            var client = CreateClient();
            var url = $"tv/{tmdbId}/season/{season}/episode/{episode}?api_key={ApiKey}";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetPropertyOrDefault("name") ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get episode title for TMDB {TmdbId} S{Season}E{Episode}", tmdbId, season, episode);
        }

        return string.Empty;
    }

    public async Task<(string title, int? year, string? posterPath)> FuzzyResolveAsync(string title, string type)
    {
        // Try typed search first
        var result = await TypedSearchAsync(title, type);
        if (result != null) return result.Value;

        // Fallback to multi-search
        result = await MultiSearchFallbackAsync(title, type);
        if (result != null) return result.Value;

        // Shorten title and try again
        var words = title.Split(' ');
        if (words.Length > 2)
        {
            var shortened = string.Join(' ', words.Take(words.Length / 2 + 1));
            result = await MultiSearchFallbackAsync(shortened, type);
            if (result != null) return result.Value;
        }

        throw new InvalidOperationException($"Could not resolve title: {title}");
    }

    private async Task<(string title, int? year, string? posterPath)?> TypedSearchAsync(string query, string type)
    {
        try
        {
            var client = CreateClient();
            var searchType = type == "movie" ? "movie" : "tv";
            var url = $"search/{searchType}?api_key={ApiKey}&query={HttpUtility.UrlEncode(query)}";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var results = json.GetProperty("results");
            if (results.GetArrayLength() > 0)
            {
                var first = results[0];
                var t = type == "movie" ? first.GetPropertyOrDefault("title") : first.GetPropertyOrDefault("name");
                var dateStr = type == "movie" ? first.GetPropertyOrDefault("release_date") : first.GetPropertyOrDefault("first_air_date");
                return (t ?? query, ParseYear(dateStr), first.GetPropertyOrDefault("poster_path"));
            }
        }
        catch { /* non-critical */ }

        return null;
    }

    private async Task<(string title, int? year, string? posterPath)?> MultiSearchFallbackAsync(string query, string type)
    {
        try
        {
            var client = CreateClient();
            var url = $"search/multi?api_key={ApiKey}&query={HttpUtility.UrlEncode(query)}";
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var results = json.GetProperty("results");
            foreach (var result in results.EnumerateArray())
            {
                var mediaType = result.GetProperty("media_type").GetString();
                if (mediaType != type) continue;

                var t = type == "movie" ? result.GetPropertyOrDefault("title") : result.GetPropertyOrDefault("name");
                var dateStr = type == "movie" ? result.GetPropertyOrDefault("release_date") : result.GetPropertyOrDefault("first_air_date");
                return (t ?? query, ParseYear(dateStr), result.GetPropertyOrDefault("poster_path"));
            }
        }
        catch { /* non-critical */ }

        return null;
    }

    private static int? ParseYear(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr) || dateStr.Length < 4) return null;
        return int.TryParse(dateStr[..4], out var year) ? year : null;
    }
}

internal static class JsonElementExtensions
{
    public static string? GetPropertyOrDefault(this JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind != JsonValueKind.Null)
            return prop.GetString();
        return null;
    }
}
