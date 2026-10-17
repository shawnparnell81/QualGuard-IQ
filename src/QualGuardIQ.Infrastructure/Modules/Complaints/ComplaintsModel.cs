using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<Complaint> Complaints => Set<Complaint>();

    private static void ConfigureComplaints(ModelBuilder b)
    {
        b.Entity<Complaint>(e =>
        {
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.HasIndex(x => x.CustomerId);
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(10000);
            foreach (var p in new[] { nameof(Complaint.CustomerContact), nameof(Complaint.CustomerReference), nameof(Complaint.PartNumber), nameof(Complaint.LotNumber), nameof(Complaint.BatchNumber), nameof(Complaint.SerialNumber), nameof(Complaint.FailureMode), nameof(Complaint.ReturnAuthorization) })
                e.Property(p).HasMaxLength(200);
            e.Property(x => x.PartRevision).HasMaxLength(10);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.ReturnQuantity).HasPrecision(18, 4);
            e.HasOne<Domain.MasterData.Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Domain.MasterData.Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Domain.MasterData.Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Domain.MasterData.Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_complaint_qty", "\"Quantity\" IS NULL OR \"Quantity\" >= 0"));
        });
    }
}
