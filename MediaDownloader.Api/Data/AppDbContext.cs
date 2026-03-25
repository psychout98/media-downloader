using MediaDownloader.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MediaDownloader.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Title> Titles => Set<Title>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<WatchProgress> WatchProgress => Set<WatchProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Title>(entity =>
        {
            entity.HasMany(t => t.MediaItems)
                .WithOne(m => m.Title)
                .HasForeignKey(m => m.TitleId);

            entity.HasMany(t => t.Jobs)
                .WithOne(j => j.Title)
                .HasForeignKey(j => j.TitleId);
        });

        modelBuilder.Entity<MediaItem>(entity =>
        {
            entity.HasOne(m => m.Job)
                .WithMany(j => j.MediaItems)
                .HasForeignKey(m => m.JobId)
                .IsRequired(false);

            entity.HasOne(m => m.WatchProgress)
                .WithOne(w => w.MediaItem)
                .HasForeignKey<WatchProgress>(w => w.MediaItemId);
        });

        modelBuilder.Entity<WatchProgress>(entity =>
        {
            entity.HasKey(w => w.MediaItemId);
        });
    }
}
