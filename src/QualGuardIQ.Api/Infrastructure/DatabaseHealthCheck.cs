using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Infrastructure;

/// <summary>
/// Unhealthy when the database can't be reached or has migrations that weren't applied (the migrate step was skipped): the API
/// would then answer sign-in but fail every signed-in request. Once the database is up to date the result is remembered, because
/// migrations never disappear while the API runs.
/// </summary>
public sealed class DatabaseHealthCheck(AppDbContext db, ILogger<DatabaseHealthCheck> log) : IHealthCheck
{
    public const string OutOfDate = "Database is out of date: run the migrate step";
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> UpToDate = new();

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct)) return HealthCheckResult.Unhealthy("Database unreachable");
        var key = db.Database.GetConnectionString() ?? "";
        if (UpToDate.ContainsKey(key)) return HealthCheckResult.Healthy();
        var pending = (await PendingAsync(db, ct)).ToList();
        if (pending.Count > 0)
        {
            log.LogError("{Message} ({Count} pending, the newest is {Latest}).", OutOfDate, pending.Count, pending[^1]);
            return HealthCheckResult.Unhealthy($"{OutOfDate} ({pending.Count} pending).");
        }
        UpToDate[key] = true;
        return HealthCheckResult.Healthy();
    }

    /// <summary>Migrations in the code that the database hasn't applied; all of them when the history table doesn't exist yet.</summary>
    public static async Task<IEnumerable<string>> PendingAsync(AppDbContext db, CancellationToken ct) =>
        await db.Database.GetPendingMigrationsAsync(ct);
}
