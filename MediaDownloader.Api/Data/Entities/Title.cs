using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MediaDownloader.Api.Data.Entities;

[Table("titles")]
public class Title
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("tmdb_id")]
    public int? TmdbId { get; set; }

    [Column("imdb_id")]
    public string? ImdbId { get; set; }

    [Column("title")]
    public string Name { get; set; } = string.Empty;

    [Column("year")]
    public int? Year { get; set; }

    [Column("type")]
    public string Type { get; set; } = string.Empty;

    [Column("is_anime")]
    public bool IsAnime { get; set; }

    [Column("overview")]
    public string? Overview { get; set; }

    [Column("poster_path")]
    public string? PosterPath { get; set; }

    [Column("folder_name")]
    public string? FolderName { get; set; }

    [Column("added_at")]
    public string AddedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [Column("updated_at")]
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    public ICollection<MediaItem> MediaItems { get; set; } = new List<MediaItem>();
    public ICollection<Job> Jobs { get; set; } = new List<Job>();
}
