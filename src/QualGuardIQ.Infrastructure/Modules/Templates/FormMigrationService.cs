using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Templates;

public sealed record MigrationCandidate(Guid DocumentId, string Number, string Title, string SiteCode, string Revision, Guid RevisionId, int SchemaVersion,
    int FieldCount, int RecordCount, Guid? OpenMigrationId, string? OpenMigrationNumber);
public sealed record StartMigrationRequest(Guid DocumentId, string? ChangeSummary);
public sealed record RollbackMigrationRequest(string Reason);
public sealed record MigrationSummary(Guid Id, string Number, Guid DocumentId, string DocumentNumber, string Title, string SourceRevision, string TargetRevision,
    int FromSchemaVersion, int ToSchemaVersion, FormMigrationStatus Status, int FieldCount, int ChangedCount, int RecordCount, DateTimeOffset CreatedAt, string CreatedBy);
public sealed record MigrationDetail(MigrationSummary Summary, Guid SourceRevisionId, Guid TargetRevisionId, RevisionStatus TargetRevisionStatus, JsonNode? Comparison,
    string SnapshotSha256, bool SnapshotIntact, DateTimeOffset? RolledBackAt, string? RolledBackBy, string? RollbackReason, Guid? RollbackRevisionId, string? RollbackRevision,
    IReadOnlyList<string> Allowed);

/// <summary>
/// Opt-in form schema migration wizard (TECH 17). Existing templates are never converted automatically. For each template migrated:
/// the original revision is kept unchanged and snapshotted (with a hash), the converted definition becomes a new draft revision, a field-by-field
/// comparison is stored, the revision goes through the normal form approval route, and the migration can be rolled back.
/// Completed records keep the revision they were filled against.
/// </summary>
public sealed class FormMigrationService(AppDbContext db, IAccessService access, ICurrentUser current, IRecordRegistry records, DocumentService docs, INumberingService numbers, IClock clock)
{
    public const string RecordType = "FormMigration";
    public const int CurrentSchemaVersion = 2;
    private Guid Me => current.UserId ?? throw new ForbiddenException();

    private async Task<IQueryable<ControlledDocument>> TemplatesAsync(CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(Me, Permissions.FormsView, ct);
        if (scope.IsEmpty) throw new ForbiddenException();
        var q = db.Documents.AsNoTracking().Where(d => d.Kind == DocumentKind.FormTemplate && !d.IsArchived);
        if (!scope.AllSites)
        {
            var sites = scope.SiteIds.ToList();
            var pairs = scope.DepartmentPairs.Select(p => p.Site.ToString() + "|" + p.Dept.ToString()).ToList();
            q = q.Where(d => sites.Contains(d.SiteId) || (d.DepartmentId != null && pairs.Contains(d.SiteId.ToString() + "|" + d.DepartmentId.ToString())));
        }
        return await q.VisibleToAsync(access, Me, ct);
    }

    /// <summary>Effective templates still on an earlier schema version.</summary>
    public async Task<IReadOnlyList<MigrationCandidate>> CandidatesAsync(CancellationToken ct)
    {
        var docsQ = await TemplatesAsync(ct);
        var rows = await docsQ.Where(d => d.Status == DocumentStatus.Active)
            .SelectMany(d => d.Revisions.Where(r => r.Status == RevisionStatus.Effective && r.SchemaVersion < CurrentSchemaVersion), (d, r) => new
            {
                d.Id, d.Number, d.Title, Site = db.Sites.Where(s => s.Id == d.SiteId).Select(s => s.Code).First(), r.Revision, RevisionId = r.Id, r.SchemaVersion, r.FieldSchema,
                Records = db.FormRecords.Count(f => f.TemplateRevisionId == r.Id),
                Open = db.FormMigrations.Where(m => m.DocumentId == d.Id && (m.Status == FormMigrationStatus.Draft || m.Status == FormMigrationStatus.InReview)).Select(m => new { m.Id, m.Number }).FirstOrDefault(),
            }).OrderBy(x => x.Number).ToListAsync(ct);
        return rows.Select(x => new MigrationCandidate(x.Id, x.Number, x.Title, x.Site, x.Revision, x.RevisionId, x.SchemaVersion, CountFields(x.FieldSchema), x.Records, x.Open?.Id, x.Open?.Number)).ToList();
    }

    private static int CountFields(string? schema)
    {
        if (string.IsNullOrWhiteSpace(schema)) return 0;
        try { return FormDefinitions.Parse(schema).Fields.Count; } catch (AppException) { return 0; }
    }

    public async Task<IReadOnlyList<MigrationSummary>> ListAsync(Guid? documentId, CancellationToken ct)
    {
        var visible = (await TemplatesAsync(ct)).Select(d => d.Id);
        var q = db.FormMigrations.AsNoTracking().Where(m => visible.Contains(m.DocumentId));
        if (documentId is { } id) q = q.Where(m => m.DocumentId == id);
        var ids = await q.OrderByDescending(m => m.CreatedAt).Select(m => m.Id).Take(500).ToListAsync(ct);
        await SyncAsync(ids, ct);
        return await SummariesAsync(db.FormMigrations.AsNoTracking().Where(m => ids.Contains(m.Id)).OrderByDescending(m => m.CreatedAt), ct);
    }

    private Task<List<MigrationSummary>> SummariesAsync(IQueryable<FormMigration> q, CancellationToken ct) =>
        q.Select(m => new MigrationSummary(m.Id, m.Number, m.DocumentId, db.Documents.Where(d => d.Id == m.DocumentId).Select(d => d.Number).First(),
            db.Documents.Where(d => d.Id == m.DocumentId).Select(d => d.Title).First(), m.SourceRevision, m.TargetRevision, m.FromSchemaVersion, m.ToSchemaVersion, m.Status,
            m.FieldCount, m.ChangedCount, m.RecordCount, m.CreatedAt, db.Users.Where(u => u.Id == m.CreatedById).Select(u => u.DisplayName).FirstOrDefault() ?? "")).ToListAsync(ct);

    public async Task<MigrationDetail> GetAsync(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        await SyncAsync([m.Id], ct);
        m = await db.FormMigrations.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var summary = (await SummariesAsync(db.FormMigrations.AsNoTracking().Where(x => x.Id == id), ct)).Single();
        var target = await db.DocumentRevisions.AsNoTracking().FirstAsync(r => r.Id == m.TargetRevisionId, ct);
        var source = await db.DocumentRevisions.AsNoTracking().FirstAsync(r => r.Id == m.SourceRevisionId, ct);
        var doc = await db.Documents.AsNoTracking().FirstAsync(d => d.Id == m.DocumentId, ct);
        var rollbackRev = m.RollbackRevisionId is { } rr ? await db.DocumentRevisions.AsNoTracking().Where(r => r.Id == rr).Select(r => r.Revision).FirstOrDefaultAsync(ct) : null;
        var canEdit = await access.CanAccessAsync(Me, Permissions.FormsEdit, doc.SiteId, doc.DepartmentId, ct);
        var canApprove = await access.CanAccessAsync(Me, Permissions.FormsApprove, doc.SiteId, doc.DepartmentId, ct);
        var allowed = new List<string>();
        if (m.Status == FormMigrationStatus.Draft && canEdit) allowed.Add("submit");
        if (m.Status is FormMigrationStatus.Draft or FormMigrationStatus.Approved or FormMigrationStatus.Effective && canApprove) allowed.Add("rollback");
        return new MigrationDetail(summary, m.SourceRevisionId, m.TargetRevisionId, target.Status, JsonNode.Parse(m.Comparison), m.SnapshotSha256,
            Sha(source.FieldSchema ?? "") == m.SnapshotSha256, m.RolledBackAt,
            m.RolledBackById is { } by ? await db.Users.Where(u => u.Id == by).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) : null, m.RollbackReason, m.RollbackRevisionId, rollbackRev, allowed);
    }

    /// <summary>The migration snapshot exactly as captured (the original definition, its hash and the records filled against it).</summary>
    public async Task<JsonNode?> SnapshotAsync(Guid id, CancellationToken ct) => JsonNode.Parse((await LoadAsync(id, ct)).Snapshot);

    private async Task<FormMigration> LoadAsync(Guid id, CancellationToken ct)
    {
        var m = await db.FormMigrations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Form migration");
        await records.RequireViewAsync(DocumentRecordTypes.FormTemplate, m.DocumentId, ct);
        return m;
    }

    /// <summary>Starts a migration: snapshot, new draft revision with the converted definition, and the comparison.</summary>
    public async Task<MigrationDetail> StartAsync(StartMigrationRequest req, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == req.DocumentId && d.Kind == DocumentKind.FormTemplate, ct) ?? throw new NotFoundException("Form template");
        await records.RequireEditAsync(DocumentRecordTypes.FormTemplate, doc.Id, ct);
        var source = await db.DocumentRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.DocumentId == doc.Id && r.Status == RevisionStatus.Effective, ct)
            ?? throw new AppException("Only a template with an effective revision can be migrated.");
        if (string.IsNullOrWhiteSpace(source.FieldSchema)) throw new AppException("The effective revision has no form definition.");
        var original = FormDefinitions.Parse(source.FieldSchema);
        if (original.SchemaVersion >= CurrentSchemaVersion) throw new AppException($"{doc.Number} revision {source.Revision} already uses the current form schema.");
        if (await db.FormMigrations.AnyAsync(m => m.DocumentId == doc.Id && (m.Status == FormMigrationStatus.Draft || m.Status == FormMigrationStatus.InReview), ct))
            throw new ConflictException($"{doc.Number} already has a migration in progress.");

        var converted = Convert(original, doc.Title);
        var convertedJson = FormDefinitions.Serialize(converted);
        FormDefinitions.Parse(convertedJson); // the converted definition must validate exactly like one saved in the designer
        var comparison = Compare(original, converted);
        var recordCount = await db.FormRecords.CountAsync(r => r.TemplateRevisionId == source.Id, ct);
        var snapshot = new JsonObject
        {
            ["documentId"] = doc.Id.ToString(), ["number"] = doc.Number, ["title"] = doc.Title, ["revisionId"] = source.Id.ToString(), ["revision"] = source.Revision,
            ["revisionStatus"] = source.Status.ToString(), ["effectiveDate"] = source.EffectiveDate?.ToString("yyyy-MM-dd"), ["schemaVersion"] = original.SchemaVersion,
            ["fieldSchema"] = source.FieldSchema, ["sha256"] = Sha(source.FieldSchema), ["records"] = recordCount, ["capturedAt"] = clock.UtcNow.ToString("O"),
        };

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var summary = string.IsNullOrWhiteSpace(req.ChangeSummary)
            ? $"Form schema migrated from version {original.SchemaVersion} to {CurrentSchemaVersion}; revision {source.Revision} is preserved unchanged."
            : req.ChangeSummary.Trim();
        // a draft left by a rolled-back migration (holding the original definition again) is reused instead of starting another revision
        var leftover = await db.DocumentRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.DocumentId == doc.Id && r.Status == RevisionStatus.Draft
            && db.FormMigrations.Any(m => m.TargetRevisionId == r.Id && m.Status == FormMigrationStatus.RolledBack), ct);
        var target = leftover ?? await docs.ReviseAsync(doc.Id, null, summary, ct);
        await docs.UpdateRevisionAsync(target.Id, new UpdateRevisionRequest(summary, null, target.TrainingRequired, target.RowVersion, convertedJson), ct);
        var m = new FormMigration
        {
            Number = await numbers.NextAsync(RecordType, doc.SiteId, ct), DocumentId = doc.Id, SiteId = doc.SiteId,
            SourceRevisionId = source.Id, SourceRevision = source.Revision, TargetRevisionId = target.Id, TargetRevision = target.Revision,
            FromSchemaVersion = original.SchemaVersion, ToSchemaVersion = CurrentSchemaVersion, Status = FormMigrationStatus.Draft,
            Snapshot = snapshot.ToJsonString(), SnapshotSha256 = Sha(source.FieldSchema), Comparison = comparison.ToJsonString(),
            FieldCount = original.Fields.Count, ChangedCount = comparison["summary"]!["changed"]!.GetValue<int>(), RecordCount = recordCount,
        };
        db.FormMigrations.Add(m);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(m.Id, ct);
    }

    /// <summary>Sends the migrated revision through the template's normal approval route.</summary>
    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        await SyncAsync([m.Id], ct);
        if (m.Status != FormMigrationStatus.Draft) throw new ConflictException("Only a draft migration can be submitted for approval.");
        await docs.SubmitAsync(m.TargetRevisionId, ct);
        await SyncAsync([m.Id], ct);
    }

    /// <summary>
    /// Rolls a migration back. A draft revision gets the original definition back; an approved or effective one is followed by a new draft
    /// revision restoring the snapshot, approved through the normal route. Records are never moved between revisions.
    /// </summary>
    public async Task<MigrationDetail> RollbackAsync(Guid id, RollbackMigrationRequest req, CancellationToken ct)
    {
        var m = await LoadAsync(id, ct);
        await SyncAsync([m.Id], ct);
        if (string.IsNullOrWhiteSpace(req.Reason)) throw new AppException("A reason is required.");
        if (req.Reason.Trim().Length > 2000) throw new AppException("The reason is too long (2,000 characters at most).");
        if (!await access.CanAccessAsync(Me, Permissions.FormsApprove, m.SiteId, (await db.Documents.AsNoTracking().FirstAsync(d => d.Id == m.DocumentId, ct)).DepartmentId, ct)) throw new ForbiddenException();
        if (m.Status == FormMigrationStatus.RolledBack) throw new ConflictException("This migration has already been rolled back.");
        if (m.Status == FormMigrationStatus.InReview) throw new ConflictException("The migrated revision is in review; return it to draft before rolling back.");
        var snap = JsonNode.Parse(m.Snapshot)!;
        var originalSchema = snap["fieldSchema"]!.GetValue<string>();
        if (Sha(originalSchema) != m.SnapshotSha256) throw new AppException("The migration snapshot does not match its recorded hash; it cannot be restored.");
        var summary = $"Rollback of migration {m.Number}: definition of revision {m.SourceRevision} restored. {req.Reason.Trim()}";
        if (summary.Length > 2000) summary = summary[..2000];

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var target = await db.DocumentRevisions.AsNoTracking().FirstAsync(r => r.Id == m.TargetRevisionId, ct);
        if (target.Status == RevisionStatus.Draft)
            await docs.UpdateRevisionAsync(target.Id, new UpdateRevisionRequest(summary, target.EffectiveDate, target.TrainingRequired, target.RowVersion, originalSchema), ct, preAuthorized: true);
        else
        {
            var restore = await docs.ReviseAsync(m.DocumentId, null, summary, ct, preAuthorized: true);
            await docs.UpdateRevisionAsync(restore.Id, new UpdateRevisionRequest(summary, null, restore.TrainingRequired, restore.RowVersion, originalSchema), ct, preAuthorized: true);
            m.RollbackRevisionId = restore.Id;
        }
        m.Status = FormMigrationStatus.RolledBack;
        m.RolledBackAt = clock.UtcNow;
        m.RolledBackById = Me;
        m.RollbackReason = req.Reason.Trim();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(m.Id, ct);
    }

    /// <summary>Migration status follows the migrated revision through the approval route.</summary>
    private async Task SyncAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var rows = await db.FormMigrations.Where(m => ids.Contains(m.Id) && m.Status != FormMigrationStatus.RolledBack)
            .Select(m => new { M = m, S = db.DocumentRevisions.Where(r => r.Id == m.TargetRevisionId).Select(r => r.Status).First() }).ToListAsync(ct);
        var changed = false;
        foreach (var x in rows)
        {
            var s = x.S switch
            {
                RevisionStatus.Draft => FormMigrationStatus.Draft,
                RevisionStatus.InReview => FormMigrationStatus.InReview,
                RevisionStatus.Approved => FormMigrationStatus.Approved,
                _ => FormMigrationStatus.Effective,
            };
            if (x.M.Status != s) { x.M.Status = s; changed = true; }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- conversion and comparison

    /// <summary>Version 1 (a field list) to version 2: every field keeps its settings, formulas, options and conditions and is placed in one explicit section.</summary>
    public static FormDefinition Convert(FormDefinition original, string title)
    {
        var copy = JsonSerializer.Deserialize<List<FormField>>(JsonSerializer.Serialize(original.Fields, FormDefinitions.Json), FormDefinitions.Json)!;
        foreach (var f in copy) f.Section = "main";
        return new FormDefinition
        {
            SchemaVersion = CurrentSchemaVersion,
            Sections = [new FormSection { Key = "main", Title = string.IsNullOrWhiteSpace(title) ? "Form" : title.Trim().Length > 200 ? title.Trim()[..200] : title.Trim(), Columns = 2 }],
            Fields = copy,
        };
    }

    public static JsonObject Compare(FormDefinition before, FormDefinition after)
    {
        var fields = new JsonArray();
        int kept = 0, changed = 0, removed = 0, added = 0, formulas = 0, options = 0, required = 0, conditions = 0;
        var afterByKey = after.Fields.ToDictionary(f => f.Key);
        foreach (var b in before.Fields)
        {
            if (!afterByKey.TryGetValue(b.Key, out var a))
            {
                removed++;
                fields.Add(new JsonObject { ["key"] = b.Key, ["label"] = b.Label, ["type"] = b.Type, ["change"] = "removed", ["before"] = Facts(b, null), ["after"] = null });
                continue;
            }
            var same = Settings(b) == Settings(a);
            if (same) kept++; else changed++;
            if (!string.IsNullOrEmpty(b.Formula) && b.Formula == a.Formula) formulas++;
            if (b.Options is { Count: > 0 } && a.Options is not null && b.Options.SequenceEqual(a.Options)) options++;
            if (b.Required && a.Required) required++;
            if ((b.VisibleWhen is not null || b.RequiredWhen is not null) && same) conditions++;
            fields.Add(new JsonObject
            {
                ["key"] = b.Key, ["label"] = b.Label, ["type"] = b.Type, ["change"] = same ? (b.Section != a.Section ? "placed" : "unchanged") : "changed",
                ["before"] = Facts(b, before), ["after"] = Facts(a, after),
            });
        }
        foreach (var a in after.Fields.Where(a => before.Fields.All(b => b.Key != a.Key)))
        {
            added++;
            fields.Add(new JsonObject { ["key"] = a.Key, ["label"] = a.Label, ["type"] = a.Type, ["change"] = "added", ["before"] = null, ["after"] = Facts(a, after) });
        }
        return new JsonObject
        {
            ["fromSchemaVersion"] = before.SchemaVersion, ["toSchemaVersion"] = after.SchemaVersion,
            ["sections"] = new JsonArray(after.Sections.Select(s => (JsonNode)new JsonObject { ["key"] = s.Key, ["title"] = s.Title, ["fields"] = after.Fields.Count(f => f.Section == s.Key) }).ToArray()),
            ["summary"] = new JsonObject
            {
                ["fields"] = before.Fields.Count, ["unchanged"] = kept, ["changed"] = changed + removed + added, ["removed"] = removed, ["added"] = added,
                ["formulasKept"] = formulas, ["formulas"] = before.Fields.Count(f => !string.IsNullOrEmpty(f.Formula)),
                ["optionListsKept"] = options, ["optionLists"] = before.Fields.Count(f => f.Options is { Count: > 0 }),
                ["requiredKept"] = required, ["required"] = before.Fields.Count(f => f.Required),
                ["conditionsKept"] = conditions, ["conditions"] = before.Fields.Count(f => f.VisibleWhen is not null || f.RequiredWhen is not null),
            },
            ["fields"] = fields,
        };
    }

    /// <summary>Every setting of a field except the section it is placed in.</summary>
    private static string Settings(FormField f)
    {
        var node = JsonSerializer.SerializeToNode(f, FormDefinitions.Json)!.AsObject();
        node.Remove("section");
        return node.ToJsonString();
    }

    private static JsonObject Facts(FormField f, FormDefinition? d) => new()
    {
        ["section"] = f.Section is null ? null : d?.Sections.FirstOrDefault(s => s.Key == f.Section)?.Title ?? f.Section,
        ["required"] = f.Required, ["formula"] = f.Formula, ["options"] = f.Options is null ? null : new JsonArray(f.Options.Select(o => (JsonNode)JsonValue.Create(o)!).ToArray()),
        ["unit"] = f.Unit, ["min"] = f.Min, ["max"] = f.Max, ["conditional"] = f.VisibleWhen is not null || f.RequiredWhen is not null,
    };

    private static string Sha(string? s) => System.Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s ?? "")));
}
