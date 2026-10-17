using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Shared;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<VideoRendition> VideoRenditions => Set<VideoRendition>();

    private static void ConfigureVideo(ModelBuilder b)
    {
        b.Entity<VideoRendition>(e =>
        {
            e.ToTable("video_renditions"); e.Property(x => x.Error).HasMaxLength(2000); e.Property(x => x.StorageKey).HasMaxLength(300); e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Sha256).HasMaxLength(64); e.Property(x => x.VideoCodec).HasMaxLength(50); e.Property(x => x.AudioCodec).HasMaxLength(50); e.Property(x => x.SourceVideoCodec).HasMaxLength(50); e.Property(x => x.Converter).HasMaxLength(200);
            e.Property(x => x.DurationSeconds).HasPrecision(12, 3); e.Property(x => x.SourceDurationSeconds).HasPrecision(12, 3);
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.AttachmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.AttachmentId, x.Format }).IsUnique(); e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
    }
}
