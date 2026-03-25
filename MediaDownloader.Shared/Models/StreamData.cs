namespace MediaDownloader.Shared.Models;

public class StreamData
{
    public MediaInfoData Media { get; set; } = new();
    public StreamInfoData Stream { get; set; } = new();
}

public class MediaInfoData
{
    public int TmdbId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool IsAnime { get; set; }
    public string? ImdbId { get; set; }
    public string? PosterPath { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public string? Overview { get; set; }
}

public class StreamInfoData
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? InfoHash { get; set; }
    public long SizeBytes { get; set; }
    public bool IsCachedRd { get; set; }
    public int Seeders { get; set; }
}
