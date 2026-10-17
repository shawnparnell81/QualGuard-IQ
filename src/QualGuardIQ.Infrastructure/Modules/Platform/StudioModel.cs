using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Platform;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<FormComponent> FormComponents => Set<FormComponent>();

    private static void ConfigureStudio(ModelBuilder b)
    {
        b.Entity<FormComponent>(e =>
        {
            e.ToTable("form_components"); e.Property(x => x.Key).HasMaxLength(50); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(2000); e.Property(x => x.FieldsJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Key, x.Version }).IsUnique();
            e.HasIndex(x => x.Key, "ux_form_component_published").IsUnique().HasFilter("\"Status\" = 1");
        });
    }
}
