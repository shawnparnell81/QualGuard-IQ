using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Quality;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<CarrierConnection> CarrierConnections => Set<CarrierConnection>();
    public DbSet<CarrierRequestLog> CarrierRequestLogs => Set<CarrierRequestLog>();
    public DbSet<CarrierLabel> CarrierLabels => Set<CarrierLabel>();
    public DbSet<CarrierTrackingEvent> CarrierTrackingEvents => Set<CarrierTrackingEvent>();

    private static void ConfigureCarrierIntegration(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict, C = DeleteBehavior.Cascade;
        b.Entity<CarrierConnection>(e =>
        {
            e.ToTable("carrier_connections");
            e.HasIndex(x => new { x.SiteId, x.CarrierId });
            e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.BaseUrl).HasMaxLength(500); e.Property(x => x.AccountNumber).HasMaxLength(100); e.Property(x => x.ClientId).HasMaxLength(200);
            e.Property(x => x.OptionsJson).HasColumnType("jsonb"); e.Property(x => x.ShipperCountry).HasMaxLength(2); e.Property(x => x.ShipperPostalCode).HasMaxLength(20);
            e.Property(x => x.VerifiedFingerprint).HasMaxLength(64); e.Property(x => x.ApprovedFingerprint).HasMaxLength(64); e.Property(x => x.LastTestMessage).HasMaxLength(1000);
            e.HasOne<Carrier>().WithMany().HasForeignKey(x => x.CarrierId).OnDelete(R);
            UserReq(e, x => x.VerifiedById); UserReq(e, x => x.ApprovedById); SiteRefs(e);
        });
        b.Entity<CarrierRequestLog>(e =>
        {
            e.ToTable("carrier_request_logs");
            e.HasIndex(x => new { x.ConnectionId, x.At }); e.HasIndex(x => x.ShipmentId);
            e.Property(x => x.ErrorCode).HasMaxLength(60); e.Property(x => x.Message).HasMaxLength(2000); e.Property(x => x.IdempotencyKey).HasMaxLength(100); e.Property(x => x.ConfigFingerprint).HasMaxLength(64);
            e.HasOne<CarrierConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(R);
            e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(R);
            UserReq(e, x => x.ById);
        });
        b.Entity<CarrierLabel>(e =>
        {
            e.ToTable("carrier_labels");
            e.HasIndex(x => x.ShipmentId); e.HasIndex(x => x.TrackingNumber);
            e.Property(x => x.TrackingNumber).HasMaxLength(100); e.Property(x => x.CarrierShipmentId).HasMaxLength(200); e.Property(x => x.ServiceCode).HasMaxLength(60);
            e.Property(x => x.Format).HasMaxLength(10); e.Property(x => x.Currency).HasMaxLength(3); e.Property(x => x.Charge).HasPrecision(18, 2); e.Property(x => x.VoidMessage).HasMaxLength(1000);
            e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(C);
            e.HasOne<CarrierConnection>().WithMany().HasForeignKey(x => x.ConnectionId).OnDelete(R);
            e.HasOne<ShipmentContainer>().WithMany().HasForeignKey(x => x.ContainerId).OnDelete(R);
        });
        b.Entity<CarrierTrackingEvent>(e =>
        {
            e.ToTable("carrier_tracking_events");
            e.HasIndex(x => new { x.ShipmentId, x.TrackingNumber, x.OccurredAt, x.Code }).IsUnique();
            e.Property(x => x.TrackingNumber).HasMaxLength(100); e.Property(x => x.Code).HasMaxLength(60); e.Property(x => x.Description).HasMaxLength(500); e.Property(x => x.Location).HasMaxLength(200);
            e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(C);
        });
    }
}
