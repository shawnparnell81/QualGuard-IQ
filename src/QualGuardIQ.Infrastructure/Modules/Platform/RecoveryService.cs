using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Infrastructure.Modules.Enterprise;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

public sealed record BackupInput(BackupKind Kind, string Label, string Location, string Method, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, long SizeBytes, string? Sha256, BackupState State, JsonElement? Fingerprint);
public sealed record BackupVerifyInput(bool Passed, string Notes);
public sealed record ObjectiveInput(string Scenario, int RtoMinutes, int RpoMinutes, Guid? ProcedureDocumentId);

/// <summary>
/// Disaster recovery center: backups registered by the backup tooling, recovery objectives with controlled procedures (document control),
/// recovery tests (the existing continuity exercises) and a recoverability verdict computed from all of them.
/// </summary>
public sealed class RecoveryService(AppDbContext db, ModuleSupport m, GovernanceService governance)
{
    /// <summary>Key tables whose row counts make up the fingerprint compared after a test restore.</summary>
    public static readonly string[] FingerprintTables = ["audit_entries", "documents", "document_revisions", "ncrs", "capas", "audits", "approval_requests", "approval_decisions", "AspNetUsers"];

    public async Task<object> FingerprintAsync(CancellationToken ct)
    {
        var migration = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
        var counts = new Dictionary<string, long>();
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        foreach (var t in FingerprintTables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT count(*) FROM qms.\"{t}\""; // fixed table list, never user input
            try { counts[t] = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)); } catch (Exception) { counts[t] = -1; }
        }
        return new { migration, counts, takenAt = m.Now };
    }

    public Task<List<BackupRecord>> BackupsAsync(CancellationToken ct) => db.BackupRecords.AsNoTracking().OrderByDescending(b => b.CompletedAt).Take(200).ToListAsync(ct);

    public async Task<BackupRecord> RegisterAsync(BackupInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Label, "Label", 200); ModuleSupport.Required(r.Location, "Location", 1000); ModuleSupport.Required(r.Method, "Method", 200);
        if (r.CompletedAt < r.StartedAt) throw new AppException("A backup cannot finish before it starts.");
        if (r.SizeBytes < 0) throw new AppException("Size cannot be negative.");
        if (r.Sha256 is { Length: > 0 } h && !System.Text.RegularExpressions.Regex.IsMatch(h, "^[0-9a-fA-F]{64}$")) throw new AppException("The checksum must be a SHA-256 hex digest.");
        var b = new BackupRecord
        {
            Kind = r.Kind, Label = r.Label.Trim(), Location = r.Location.Trim(), Method = r.Method.Trim(), StartedAt = r.StartedAt, CompletedAt = r.CompletedAt, SizeBytes = r.SizeBytes,
            Sha256 = r.Sha256?.ToLowerInvariant(), State = r.State, Fingerprint = r.Fingerprint is { ValueKind: JsonValueKind.Object } f ? f.GetRawText() : null
        };
        db.BackupRecords.Add(b); await db.SaveChangesAsync(ct);
        return b;
    }

    public async Task VerifyAsync(Guid id, BackupVerifyInput r, CancellationToken ct)
    {
        var b = await db.BackupRecords.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Backup");
        ModuleSupport.Required(r.Notes, "Verification notes", 4000);
        if (b.State == BackupState.Failed) throw new AppException("A failed backup cannot be verified.");
        b.Verification = r.Passed ? BackupVerification.Passed : BackupVerification.Failed; b.VerifiedAt = m.Now; b.VerificationNotes = r.Notes.Trim();
        await db.SaveChangesAsync(ct);
    }

    public Task<List<RecoveryObjective>> ObjectivesAsync(CancellationToken ct) => db.RecoveryObjectives.AsNoTracking().OrderBy(o => o.Scenario).ToListAsync(ct);

    public async Task<RecoveryObjective> SaveObjectiveAsync(Guid? id, ObjectiveInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Scenario, "Scenario", 200);
        if (r.RtoMinutes is < 1 or > 100000 || r.RpoMinutes is < 0 or > 100000) throw new AppException("Enter recovery time and data-loss targets in minutes.");
        if (r.ProcedureDocumentId is { } d && !await db.Documents.AnyAsync(x => x.Id == d, ct)) throw new AppException("Choose an existing controlled document as the procedure.");
        var o = id is { } x ? await db.RecoveryObjectives.FirstOrDefaultAsync(q => q.Id == x, ct) ?? throw new NotFoundException("Recovery objective") : new RecoveryObjective();
        o.Scenario = r.Scenario.Trim(); o.RtoMinutes = r.RtoMinutes; o.RpoMinutes = r.RpoMinutes; o.ProcedureDocumentId = r.ProcedureDocumentId;
        if (id is null) db.RecoveryObjectives.Add(o);
        await db.SaveChangesAsync(ct);
        return o;
    }

    /// <summary>
    /// Recoverability verdict. For each objective: the latest successful database backup must be newer than the RPO, a verified backup must exist,
    /// the latest disaster-recovery test must have passed within the RTO, and the procedure document must be effective. Live checks use the same probes as continuity exercises.
    /// </summary>
    public async Task<object> StatusAsync(CancellationToken ct)
    {
        var now = m.Now;
        var backups = await db.BackupRecords.AsNoTracking().Where(b => b.State == BackupState.Succeeded).OrderByDescending(b => b.CompletedAt).Take(500).ToListAsync(ct);
        var lastDb = backups.FirstOrDefault(b => b.Kind == BackupKind.Database);
        var lastFiles = backups.FirstOrDefault(b => b.Kind == BackupKind.FileStore);
        var lastVerified = backups.FirstOrDefault(b => b.Verification == BackupVerification.Passed);
        var tests = await db.ContinuityExercises.AsNoTracking().Where(e => e.Kind == ContinuityKind.DisasterRecoveryTest || e.Kind == ContinuityKind.BackupVerification).OrderByDescending(e => e.PerformedAt).Take(50).ToListAsync(ct);
        var lastTest = tests.FirstOrDefault(e => e.Kind == ContinuityKind.DisasterRecoveryTest && e.Result != ContinuityResult.Planned);
        var objectives = await db.RecoveryObjectives.AsNoTracking().ToListAsync(ct);
        var docIds = objectives.Where(o => o.ProcedureDocumentId != null).Select(o => o.ProcedureDocumentId!.Value).ToList();
        var docs = await db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id)).Select(d => new { d.Id, d.Number, d.Title, Effective = db.DocumentRevisions.Any(r => r.DocumentId == d.Id && r.Status == RevisionStatus.Effective) }).ToListAsync(ct);
        var live = await governance.SystemChecksAsync(ct);
        var gaps = new List<string>();
        if (lastDb is null) gaps.Add("No successful database backup is registered.");
        if (lastFiles is null) gaps.Add("No successful file-store backup is registered.");
        if (lastVerified is null) gaps.Add("No backup has passed a test restore.");
        if (lastTest is null) gaps.Add("No disaster-recovery test has been performed.");
        if (objectives.Count == 0) gaps.Add("No recovery objectives (RTO/RPO) are defined.");
        var perObjective = objectives.Select(o =>
        {
            var doc = docs.FirstOrDefault(d => d.Id == o.ProcedureDocumentId);
            var issues = new List<string>();
            if (lastDb is null || (now - lastDb.CompletedAt).TotalMinutes > o.RpoMinutes) issues.Add($"latest database backup is older than the {o.RpoMinutes}-minute RPO");
            if (lastTest is null || lastTest.Result == ContinuityResult.Failed) issues.Add("no passing recovery test");
            else if (lastTest.RecoveryMinutes is { } rm && rm > o.RtoMinutes) issues.Add($"last test took {rm} min, above the {o.RtoMinutes}-minute RTO");
            if (doc is null) issues.Add("no controlled recovery procedure linked");
            else if (!doc.Effective) issues.Add($"procedure {doc.Number} is not effective");
            return new { o.Id, o.Scenario, o.RtoMinutes, o.RpoMinutes, Procedure = doc == null ? null : $"{doc.Number} {doc.Title}", o.ProcedureDocumentId, Met = issues.Count == 0, Issues = issues };
        }).ToList();
        foreach (var o in perObjective.Where(o => !o.Met)) gaps.Add($"{o.Scenario}: {string.Join("; ", o.Issues)}.");
        bool Ok(object? v) => v is not null && JsonSerializer.SerializeToElement(v).TryGetProperty("ok", out var p) && p.ValueKind == JsonValueKind.True;
        if (!Ok(live.GetValueOrDefault("database")) || !Ok(live.GetValueOrDefault("fileStore"))) gaps.Add("A live system check failed.");
        return new
        {
            Recoverable = gaps.Count == 0, Gaps = gaps, Live = live,
            LastDatabaseBackup = lastDb, LastFileStoreBackup = lastFiles, LastVerifiedBackup = lastVerified,
            LastRecoveryTest = lastTest is null ? null : new { lastTest.Id, lastTest.Number, lastTest.Title, lastTest.Result, lastTest.RecoveryMinutes, lastTest.DataLossMinutes, lastTest.PerformedAt },
            Objectives = perObjective
        };
    }
}
