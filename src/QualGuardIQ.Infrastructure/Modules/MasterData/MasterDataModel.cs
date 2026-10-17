using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ProductFamily> ProductFamilies => Set<ProductFamily>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<PartRevision> PartRevisions => Set<PartRevision>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<SupplierContact> SupplierContacts => Set<SupplierContact>();
    public DbSet<SupplierCertification> SupplierCertifications => Set<SupplierCertification>();
    public DbSet<SupplierOverride> SupplierOverrides => Set<SupplierOverride>();

    private static void ConfigureMasterData(ModelBuilder b)
    {
        b.Entity<ProductFamily>(e =>
        {
            e.ToTable("product_families");
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });
        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.Property(x => x.Number).HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Category).HasMaxLength(100);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasOne<ProductFamily>().WithMany().HasForeignKey(x => x.FamilyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<Product>(b);
        b.Entity<Part>(e =>
        {
            e.ToTable("parts");
            e.Property(x => x.PartNumber).HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Revision).HasMaxLength(10).IsRequired();
            e.Property(x => x.UnitOfMeasure).HasMaxLength(20).IsRequired();
            e.Property(x => x.Material).HasMaxLength(200);
            e.HasIndex(x => x.PartNumber).IsUnique();
            e.HasIndex(x => x.ProductId);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<Part>(b);
        b.Entity<PartRevision>(e =>
        {
            e.ToTable("part_revisions");
            e.Property(x => x.Revision).HasMaxLength(10).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            e.HasIndex(x => new { x.PartId, x.Revision }).IsUnique();
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContactName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Address).HasMaxLength(500);
            e.HasIndex(x => x.Code).IsUnique();
        });
        Scoped<Customer>(b);
        b.Entity<Supplier>(e =>
        {
            e.ToTable("suppliers");
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.OperatingName).HasMaxLength(200);
            e.Property(x => x.Category).HasMaxLength(100).IsRequired();
            e.Property(x => x.SuppliedScope).HasMaxLength(2000);
            e.Property(x => x.ApprovedScope).HasMaxLength(2000);
            e.Property(x => x.ProposedScope).HasMaxLength(2000);
            e.Property(x => x.StatusReason).HasMaxLength(2000);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Website).HasMaxLength(300);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        Scoped<Supplier>(b);
        b.Entity<SupplierContact>(e =>
        {
            e.ToTable("supplier_contacts");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Role).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SupplierCertification>(e =>
        {
            e.ToTable("supplier_certifications");
            e.Property(x => x.Standard).HasMaxLength(100).IsRequired();
            e.Property(x => x.CertificateNumber).HasMaxLength(100);
            e.Property(x => x.Issuer).HasMaxLength(200);
            e.HasIndex(x => new { x.SupplierId, x.ExpiresOn });
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint("ck_cert_dates", "\"IssuedOn\" IS NULL OR \"ExpiresOn\" >= \"IssuedOn\""));
        });
        b.Entity<SupplierOverride>(e =>
        {
            e.ToTable("supplier_overrides");
            e.Property(x => x.RecordType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Condition).HasMaxLength(100).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => x.SupplierId);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
