using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Platform;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<FormComponent> FormComponents => Set<FormComponent>();
    public DbSet<BusinessRule> BusinessRules => Set<BusinessRule>();
    public DbSet<RuleFiring> RuleFirings => Set<RuleFiring>();

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
    }
}
