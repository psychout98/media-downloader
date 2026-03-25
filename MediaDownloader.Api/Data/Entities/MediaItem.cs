using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MediaDownloader.Api.Data.Entities;

[Table("media_items")]
public class MediaItem
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("title_id")]
    public string TitleId { get; set; } = string.Empty;

    [Column("job_id")]
    public string? JobId { get; set; }

    [Column("season")]
    public int? Season { get; set; }

    [Column("episode")]
    public int? Episode { get; set; }

    [Column("episode_title")]
    public string? EpisodeTitle { get; set; }

    [Column("file_path")]
    public string? FilePath { get; set; }

    [Column("is_archived")]
    public bool IsArchived { get; set; }

    [Column("added_at")]
    public string AddedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [Column("updated_at")]
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [ForeignKey(nameof(TitleId))]
    public Title? Title { get; set; }

    [ForeignKey(nameof(JobId))]
    public Job? Job { get; set; }

    public WatchProgress? WatchProgress { get; set; }
}
