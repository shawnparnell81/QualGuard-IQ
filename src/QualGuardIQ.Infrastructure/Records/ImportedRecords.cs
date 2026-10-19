using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Records;

/// <summary>Historical records brought in by an import: shown as "Imported (read-only)", printable, exportable and deletable, never edited.</summary>
public static class ImportedRecords
{
    public const string Label = "Imported (read-only)";
    public const string ReadOnlyMessage = "This record was imported as a historical record and is read-only. It can be printed, exported or deleted, but not edited, signed or sent for approval.";

    public static Task<bool> IsReadOnlyAsync(AppDbContext db, Guid recordId, CancellationToken ct) =>
        db.ImportedItems.AsNoTracking().AnyAsync(i => i.TargetId == recordId && i.ReadOnly && !i.RolledBack, ct);
}
