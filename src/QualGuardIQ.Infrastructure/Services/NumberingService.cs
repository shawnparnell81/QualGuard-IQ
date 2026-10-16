using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Services;

public interface INumberingService
{
    /// <summary>Allocates the next number. Must be called inside an open transaction; the row lock is held until commit, so numbers are unique and gap-free per committed transaction.</summary>
    Task<string> NextAsync(string recordType, Guid? siteId, CancellationToken ct = default);
}

public sealed class NumberingService(AppDbContext db, IClock clock) : INumberingService
{
    public async Task<string> NextAsync(string recordType, Guid? siteId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Numbers must be allocated inside a transaction.");

        var seq = await db.NumberSequences
            .FromSqlInterpolated($"SELECT * FROM qms.number_sequences WHERE \"RecordType\" = {recordType} AND (\"SiteId\" = {siteId} OR (\"SiteId\" IS NULL AND {siteId}::uuid IS NULL)) FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (seq is null && siteId is not null)
        {
            seq = await db.NumberSequences
                .FromSqlInterpolated($"SELECT * FROM qms.number_sequences WHERE \"RecordType\" = {recordType} AND \"SiteId\" IS NULL FOR UPDATE")
                .FirstOrDefaultAsync(ct);
        }
        if (seq is null) throw new AppException($"No numbering sequence is configured for {recordType}.");

        var year = clock.UtcNow.Year;
        if (seq.ResetYearly && seq.Year != year) { seq.Year = year; seq.NextValue = 1; }
        var value = seq.NextValue++;
        await db.SaveChangesAsync(ct);
        var yearPart = seq.ResetYearly ? $"{year}-" : "";
        return $"{seq.Prefix}{yearPart}{value.ToString().PadLeft(seq.Padding, '0')}";
    }
}
