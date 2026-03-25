using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MediaDownloader.Api.Data.Entities;

[Table("watch_progress")]
public class WatchProgress
{
    [Key]
    [Column("media_item_id")]
    public string MediaItemId { get; set; } = string.Empty;

    [Column("position_ms")]
    public long PositionMs { get; set; }

    [Column("duration_ms")]
    public long DurationMs { get; set; }

    [Column("watched")]
    public bool Watched { get; set; }

    [Column("updated_at")]
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [ForeignKey(nameof(MediaItemId))]
    public MediaItem? MediaItem { get; set; }
}
