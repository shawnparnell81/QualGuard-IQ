using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Platform;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<FormComponent> FormComponents => Set<FormComponent>();
    public DbSet<BusinessRule> BusinessRules => Set<BusinessRule>();
    public DbSet<RuleFiring> RuleFirings => Set<RuleFiring>();
    public DbSet<DataDomain> DataDomains => Set<DataDomain>();
    public DbSet<DataQualityRule> DataQualityRules => Set<DataQualityRule>();
    public DbSet<DataQualitySnapshot> DataQualitySnapshots => Set<DataQualitySnapshot>();
    public DbSet<MasterDataChange> MasterDataChanges => Set<MasterDataChange>();

    private static void ConfigureStudio(ModelBuilder b)
    {
        b.Entity<FormComponent>(e =>
        {
            e.ToTable("form_components"); e.Property(x => x.Key).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(2000); e.Property(x => x.FieldsJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Key, x.Version }).IsUnique();
            e.HasIndex(x => x.Key, "ux_form_component_published").IsUnique().HasFilter("\"Status\" = 1");
        });
        b.Entity<BusinessRule>(e =>
        {
            e.ToTable("business_rules"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.RecordType).HasMaxLength(80); e.Property(x => x.Trigger).HasMaxLength(60);
            e.Property(x => x.Message).HasMaxLength(1000); e.Property(x => x.TargetPermission).HasMaxLength(100); Json(e, x => x.Conditions);
            e.HasIndex(x => new { x.RecordType, x.Kind, x.Enabled });
        });
        b.Entity<RuleFiring>(e =>
        {
            e.ToTable("rule_firings"); e.Property(x => x.RecordType).HasMaxLength(80); e.Property(x => x.Outcome).HasMaxLength(500);
            e.HasOne<BusinessRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RuleId, x.RecordId }); e.HasIndex(x => x.FiredAt);
        });
        b.Entity<DataDomain>(e => { e.ToTable("data_domains"); e.Property(x => x.Key).HasMaxLength(40); e.Property(x => x.Name).HasMaxLength(100); e.Property(x => x.Description).HasMaxLength(2000); e.HasIndex(x => x.Key).IsUnique(); });
        b.Entity<DataQualityRule>(e => { e.ToTable("data_quality_rules"); e.Property(x => x.DomainKey).HasMaxLength(40); e.Property(x => x.Field).HasMaxLength(100); e.Property(x => x.Parameter).HasMaxLength(500); e.HasIndex(x => x.DomainKey); });
        b.Entity<DataQualitySnapshot>(e => { e.ToTable("data_quality_snapshots"); e.Property(x => x.DomainKey).HasMaxLength(40); e.Property(x => x.Compliance).HasPrecision(7, 2); e.HasIndex(x => new { x.DomainKey, x.RunAt }); });
        b.Entity<MasterDataChange>(e =>
        {
            e.ToTable("master_data_changes"); e.Property(x => x.Number).HasMaxLength(40); e.Property(x => x.DomainKey).HasMaxLength(40); e.Property(x => x.RecordLabel).HasMaxLength(300);
            e.Property(x => x.Reason).HasMaxLength(2000); Json(e, x => x.Changes); Json(e, x => x.Previous);
            e.HasIndex(x => x.Number); e.HasIndex(x => new { x.DomainKey, x.RecordId });
        });
    }
}
