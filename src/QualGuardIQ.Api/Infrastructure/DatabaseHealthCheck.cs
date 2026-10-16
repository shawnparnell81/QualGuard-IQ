using Microsoft.Extensions.Diagnostics.HealthChecks;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Infrastructure;

public sealed class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unreachable");
}
