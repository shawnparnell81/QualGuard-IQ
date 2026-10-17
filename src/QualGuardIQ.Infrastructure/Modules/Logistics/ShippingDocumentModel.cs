using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Quality;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ShippingDocumentTemplate> ShippingDocumentTemplates => Set<ShippingDocumentTemplate>();
    public DbSet<ShippingDocumentTemplateRevision> ShippingDocumentTemplateRevisions => Set<ShippingDocumentTemplateRevision>();
    public DbSet<GeneratedShippingDocument> GeneratedShippingDocuments => Set<GeneratedShippingDocument>();

    private static void ConfigureShippingDocuments(ModelBuilder b)
    {
        const DeleteBehavior R = DeleteBehavior.Restrict;
        b.Entity<ShippingDocumentTemplate>(e =>
        {
            e.ToTable("shipping_document_templates");
            e.HasIndex(x => x.Code).IsUnique(); e.HasIndex(x => new { x.Kind, x.SiteId });
            e.Property(x => x.Code).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Description).HasMaxLength(2000);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(R);
        });
        b.Entity<ShippingDocumentTemplateRevision>(e =>
        {
            e.ToTable("shipping_document_template_revisions");
            e.HasIndex(x => new { x.TemplateId, x.Revision }).IsUnique();
            e.Property(x => x.LayoutJson).HasColumnType("jsonb"); e.Property(x => x.ChangeSummary).HasMaxLength(2000); e.Property(x => x.ApprovalNotes).HasMaxLength(2000);
            e.Property(x => x.RejectedReason).HasMaxLength(2000); e.Property(x => x.ContentHash).HasMaxLength(64);
            e.HasOne<ShippingDocumentTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(R);
            e.HasOne<Carrier>().WithMany().HasForeignKey(x => x.CarrierId).OnDelete(R);
            UserReq(e, x => x.SubmittedById); UserReq(e, x => x.ApprovedById);
        });
        b.Entity<GeneratedShippingDocument>(e =>
        {
            e.ToTable("generated_shipping_documents");
            e.HasIndex(x => new { x.ShipmentId, x.GeneratedAt }); e.HasIndex(x => new { x.SiteId, x.Kind, x.Reference });
            e.Property(x => x.Reference).HasMaxLength(100); e.Property(x => x.TemplateCode).HasMaxLength(40); e.Property(x => x.DataJson).HasColumnType("jsonb"); e.Property(x => x.PdfSha256).HasMaxLength(64);
            e.HasOne<Shipment>().WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(R);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(R);
            e.HasOne<ShippingDocumentTemplateRevision>().WithMany().HasForeignKey(x => x.TemplateRevisionId).OnDelete(R);
            UserReq(e, x => x.GeneratedById);
        });
    }
}
