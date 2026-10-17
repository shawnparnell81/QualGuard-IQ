using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Organization;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<LabelPrinter> LabelPrinters => Set<LabelPrinter>();
    public DbSet<LabelPrintRoute> LabelPrintRoutes => Set<LabelPrintRoute>();
    public DbSet<LabelPrintJob> LabelPrintJobs => Set<LabelPrintJob>();

    private static void ConfigureLabels(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict;
        b.Entity<LabelPrinter>(e =>
        {
            e.ToTable("label_printers");
            e.HasIndex(x => new { x.SiteId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.LastError).HasMaxLength(1000); e.Property(x => x.WidthMm).HasPrecision(8, 2); e.Property(x => x.HeightMm).HasPrecision(8, 2);
            SiteRefs(e);
        });
        b.Entity<LabelPrintRoute>(e =>
        {
            e.ToTable("label_print_routes");
            e.HasIndex(x => new { x.LabelType, x.Active });
            e.Property(x => x.Workstation).HasMaxLength(100);
            e.HasOne<LabelPrinter>().WithMany().HasForeignKey(x => x.PrinterId).OnDelete(R);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(R);
            e.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(R);
            UserReq(e, x => x.UserId);
        });
        b.Entity<LabelPrintJob>(e =>
        {
            e.ToTable("label_print_jobs");
            e.HasIndex(x => new { x.RefType, x.RefId });
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.SiteId, x.RequestedAt });
            e.HasIndex(x => x.TriggerKey);
            e.Property(x => x.TriggerKey).HasMaxLength(120);
            e.Property(x => x.RefType).HasMaxLength(60); e.Property(x => x.RefNumber).HasMaxLength(100); e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Barcode).HasMaxLength(500); e.Property(x => x.DataJson).HasColumnType("jsonb"); e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.Workstation).HasMaxLength(100); e.Property(x => x.ReprintReason).HasMaxLength(500);
            e.HasOne<LabelPrinter>().WithMany().HasForeignKey(x => x.PrinterId).OnDelete(R);
            e.HasOne<LabelPrintRoute>().WithMany().HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<LabelPrintJob>().WithMany().HasForeignKey(x => x.ReprintOfId).OnDelete(R);
            e.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(R);
            UserReq(e, x => x.RequestedById);
            SiteRefs(e);
        });
    }
}
