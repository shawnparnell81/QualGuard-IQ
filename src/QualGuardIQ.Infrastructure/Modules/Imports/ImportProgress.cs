using System.Collections.Concurrent;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

/// <summary>
/// Live progress of imports that are running now. Rows are written inside one transaction, so progress saved on the job row is not visible
/// to anyone else until the import commits; this keeps the running figure where the status endpoints can read it.
/// </summary>
public sealed class ImportProgress
{
    readonly ConcurrentDictionary<Guid, (int Percent, int Rows)> running = new();
    public void Set(Guid jobId, int percent, int rows) => running[jobId] = (percent, rows);
    public void Clear(Guid jobId) => running.TryRemove(jobId, out _);
    public (int Percent, int Rows)? Get(Guid jobId) => running.TryGetValue(jobId, out var p) ? p : null;
}
