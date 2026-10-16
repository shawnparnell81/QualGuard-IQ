using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QualGuardIQ.Infrastructure.Persistence;

/// <summary>Used only by migration tooling.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("QMS_DESIGN_DB") ?? "Host=localhost;Port=5433;Database=qgiq;Username=qgiq;Password=qgiq_local_dev",
                n => n.MigrationsHistoryTable("__ef_migrations", "qms"))
            .Options);
}
