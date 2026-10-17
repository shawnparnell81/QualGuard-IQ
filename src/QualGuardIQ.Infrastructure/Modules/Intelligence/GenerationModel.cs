using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Intelligence;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<GenerationSettings> GenerationSettings => Set<GenerationSettings>();
    public DbSet<GenerationProvider> GenerationProviders => Set<GenerationProvider>();
    public DbSet<GenerationFunctionPolicy> GenerationFunctionPolicies => Set<GenerationFunctionPolicy>();
    public DbSet<GenerationRequestLog> GenerationRequestLogs => Set<GenerationRequestLog>();

    private static void ConfigureGenerationBoundary(ModelBuilder b)
    {
        b.Entity<GenerationSettings>(e => { e.ToTable("generation_settings"); Json(e, x => x.RedactionPatterns); });
        b.Entity<GenerationProvider>(e =>
        {
            e.ToTable("generation_providers"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Endpoint).HasMaxLength(500); e.Property(x => x.Model).HasMaxLength(200);
            e.Property(x => x.SecretReference).HasMaxLength(200); e.Property(x => x.ConfigFingerprint).HasMaxLength(64); e.Property(x => x.LastTestFingerprint).HasMaxLength(64); e.Property(x => x.LastTestMessage).HasMaxLength(1000);
            e.HasIndex(x => x.Name).IsUnique();
        });
        b.Entity<GenerationFunctionPolicy>(e =>
        {
            e.ToTable("generation_function_policies"); e.Property(x => x.Function).HasMaxLength(100); e.HasIndex(x => x.Function).IsUnique();
            e.HasOne<GenerationProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<GenerationRequestLog>(e =>
        {
            e.ToTable("generation_request_logs"); e.Property(x => x.Function).HasMaxLength(100); e.Property(x => x.RecordType).HasMaxLength(50); e.Property(x => x.ProviderName).HasMaxLength(200); e.Property(x => x.Model).HasMaxLength(200);
            e.Property(x => x.FallbackReason).HasMaxLength(1000); e.Property(x => x.PromptHash).HasMaxLength(64); e.Property(x => x.ResponseHash).HasMaxLength(64); Json(e, x => x.SourceRefs);
            e.HasIndex(x => x.CreatedAt); e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
    }
}
