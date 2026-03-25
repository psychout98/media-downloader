using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using MediaDownloader.Shared.Enums;

namespace MediaDownloader.Api.Data.Entities;

[Table("jobs")]
public class Job
{
    [Key]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("title_id")]
    public string TitleId { get; set; } = string.Empty;

    [Column("query")]
    public string? Query { get; set; }

    [Column("season")]
    public int? Season { get; set; }

    [Column("episode")]
    public int? Episode { get; set; }

    [Column("status")]
    public string Status { get; set; } = JobStatus.Pending.ToApiString();

    [Column("progress")]
    public double Progress { get; set; }

    [Column("size_bytes")]
    public long SizeBytes { get; set; }

    [Column("downloaded_bytes")]
    public long DownloadedBytes { get; set; }

    [Column("quality")]
    public string? Quality { get; set; }

    [Column("torrent_name")]
    public string? TorrentName { get; set; }

    [Column("rd_torrent_id")]
    public string? RdTorrentId { get; set; }

    [Column("error")]
    public string? Error { get; set; }

    [Column("log")]
    public string? Log { get; set; }

    [Column("stream_data")]
    public string? StreamData { get; set; }

    [Column("created_at")]
    public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [Column("updated_at")]
    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    [ForeignKey(nameof(TitleId))]
    public Title? Title { get; set; }

    public ICollection<MediaItem> MediaItems { get; set; } = new List<MediaItem>();

    [NotMapped]
    public JobStatus JobStatus
    {
        get => JobStatusExtensions.FromApiString(Status);
        set => Status = value.ToApiString();
    }
}
