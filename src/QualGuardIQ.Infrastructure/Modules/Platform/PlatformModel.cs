using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Platform;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<PlatformEvent> PlatformEvents => Set<PlatformEvent>();
    public DbSet<PlatformCursor> PlatformCursors => Set<PlatformCursor>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<SavedReport> SavedReports => Set<SavedReport>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<ResourceAbsence> ResourceAbsences => Set<ResourceAbsence>();

    private static void ConfigurePlatform(ModelBuilder b)
    {
        b.Entity<PlatformEvent>(e =>
        {
            e.ToTable("platform_events"); e.Property(x => x.Type).HasMaxLength(60); e.Property(x => x.Source).HasMaxLength(30); e.Property(x => x.RecordType).HasMaxLength(80);
            e.Property(x => x.UserName).HasMaxLength(200); e.Property(x => x.Description).HasMaxLength(1000); e.Property(x => x.Data).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredAt); e.HasIndex(x => new { x.RecordType, x.RecordId }); e.HasIndex(x => x.UserId);
        });
        b.Entity<PlatformCursor>(e => { e.ToTable("platform_cursors"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(60); });
        b.Entity<WebhookSubscription>(e =>
        {
            e.ToTable("webhook_subscriptions"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Url).HasMaxLength(1000); e.Property(x => x.EncryptedSecret).HasMaxLength(2000);
            Json(e, x => x.Events); Json(e, x => x.RecordTypes);
        });
        b.Entity<WebhookDelivery>(e =>
        {
            e.ToTable("webhook_deliveries"); e.Property(x => x.EventType).HasMaxLength(60); e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasOne<WebhookSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt }); e.HasIndex(x => new { x.SubscriptionId, x.EventId }).IsUnique();
        });
        b.Entity<SavedReport>(e =>
        {
            e.ToTable("saved_reports"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.ReportKey).HasMaxLength(100);
            e.Property(x => x.FilterColumn).HasMaxLength(100); e.Property(x => x.FilterValue).HasMaxLength(200); e.Property(x => x.RowField).HasMaxLength(100); e.Property(x => x.ColumnField).HasMaxLength(100); e.Property(x => x.ValueField).HasMaxLength(100);
            Json(e, x => x.Columns); e.HasIndex(x => x.OwnerId);
        });
        b.Entity<ReportSchedule>(e =>
        {
            e.ToTable("report_schedules"); e.Property(x => x.Cron).HasMaxLength(100); Json(e, x => x.Recipients);
            e.HasOne<SavedReport>().WithMany().HasForeignKey(x => x.SavedReportId).OnDelete(DeleteBehavior.Cascade); e.HasIndex(x => new { x.Enabled, x.NextRunAt });
        });
        b.Entity<Dashboard>(e => { e.ToTable("dashboards"); e.Property(x => x.Name).HasMaxLength(200); Json(e, x => x.Widgets); e.HasIndex(x => x.OwnerId); });
        b.Entity<SavedSearch>(e => { e.ToTable("saved_searches"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Text).HasMaxLength(500); e.Property(x => x.RecordType).HasMaxLength(80); e.HasIndex(x => x.OwnerId); });
        b.Entity<ResourceAbsence>(e => { e.ToTable("resource_absences"); e.Property(x => x.ResourceName).HasMaxLength(300); e.Property(x => x.Reason).HasMaxLength(500); e.HasIndex(x => new { x.ResourceId, x.From }); });
    }
}
