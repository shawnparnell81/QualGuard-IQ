using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

public sealed record TargetField(string Key, string Label, string Type, bool Required, IReadOnlyList<string>? Options = null);
public sealed record TargetInfo(string Key, string Name, string EditPermission, bool NeedsTemplate)
{
    /// <summary>Master list migration targets (Excel mode D) are chosen with the "master list" purpose; the others take records.</summary>
    public bool MasterList => Key.StartsWith("master-", StringComparison.Ordinal);
}

/// <summary>A module that rows can be imported into. Create runs inside the import transaction; Rollback undoes one created record.</summary>
public interface IImportTarget
{
    TargetInfo Info { get; }
    Task<IReadOnlyList<TargetField>> FieldsAsync(ImportJob job, CancellationToken ct);
    /// <summary>Checks one transformed row; returns the problems (empty when the row can be imported).</summary>
    Task<IReadOnlyList<string>> ValidateAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, CancellationToken ct);
    Task<IReadOnlyList<string>> DuplicateKeysAsync(ImportJob job, CancellationToken ct);
    /// <summary>True when a record with these key values already exists in the module.</summary>
    Task<bool> ExistsAsync(ImportJob job, IReadOnlyDictionary<string, string?> key, CancellationToken ct);
    Task<Guid> CreateAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, CancellationToken ct);
    /// <summary>Removes a record the import created; returns a reason when it can no longer be removed (for example it was changed or submitted).</summary>
    Task<string?> RollbackAsync(ImportJob job, Guid id, CancellationToken ct);
    string RecordType { get; }

    /// <summary>True when existing records can be updated through an import (matched on the duplicate keys).</summary>
    bool CanUpdate => false;
    /// <summary>The existing record matching these key values, for an update.</summary>
    Task<Guid?> FindAsync(ImportJob job, IReadOnlyDictionary<string, string?> key, CancellationToken ct) => Task.FromResult<Guid?>(null);
    /// <summary>The record's current values for the mapped fields (the "before" of the preview).</summary>
    Task<IReadOnlyDictionary<string, string?>> CurrentAsync(ImportJob job, Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());
    /// <summary>Updates a record with the row's values; returns the values before the update (JSON) for rollback, or a reason it cannot be updated.</summary>
    Task<(string? Before, string? Refused)> UpdateAsync(ImportJob job, Guid id, IReadOnlyDictionary<string, string?> row, CancellationToken ct) => Task.FromResult<(string?, string?)>((null, "Records of this kind are not updated by imports."));
    /// <summary>Puts back the values an import replaced; returns a reason when that is no longer possible.</summary>
    Task<string?> RevertAsync(ImportJob job, Guid id, string before, CancellationToken ct) => Task.FromResult<string?>("Updates of this kind cannot be undone.");
}

/// <summary>Rows become draft form records of an effective form template; columns map to the template's fields.</summary>
public sealed class FormRecordImportTarget(AppDbContext db, INumberingService numbers, FormValueIndexer indexer, ModuleSupport m, Application.Records.ISearchIndexer search) : IImportTarget
{
    public TargetInfo Info { get; } = new("form-records", "Form records", Permissions.FormsEdit, true);
    public string RecordType => FormService.RecordType;

    private async Task<(DocumentRevision Rev, FormDefinition Def, ControlledDocument Doc)> TemplateAsync(ImportJob job, CancellationToken ct)
    {
        if (job.TargetTemplateId is not { } t) throw new AppException("Choose the form template to import into.");
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == t && d.Kind == DocumentKind.FormTemplate, ct) ?? throw new AppException("The form template does not exist.");
        var rev = await db.DocumentRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.DocumentId == t && r.Status == RevisionStatus.Effective, ct)
            ?? throw new AppException("The form template has no effective revision.");
        return (rev, FormDefinitions.Parse(rev.FieldSchema!), doc);
    }

    public async Task<IReadOnlyList<TargetField>> FieldsAsync(ImportJob job, CancellationToken ct)
    {
        var (_, def, _) = await TemplateAsync(job, ct);
        var list = def.Fields.Where(f => f.Type is not ("calc" or "heading" or "note" or "signature")).Select(f => new TargetField(f.Key, f.Label, f.Type, f.Required, f.Options)).ToList();
        list.Insert(0, new TargetField("__title", "Record title", "text", false));
        return list;
    }

    public async Task<IReadOnlyList<string>> ValidateAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var (_, def, _) = await TemplateAsync(job, ct);
        try { FormDefinitions.Apply(def, row.Where(k => k.Key != "__title").ToDictionary(), requireComplete: true); return []; }
        catch (AppException e) { return [e.Message]; }
    }

    public Task<IReadOnlyList<string>> DuplicateKeysAsync(ImportJob job, CancellationToken ct)
    {
        var keys = JsonDocument.Parse(job.Settings).RootElement.TryGetProperty("duplicateKeys", out var k) && k.ValueKind == JsonValueKind.Array
            ? k.EnumerateArray().Select(x => x.GetString()!).Where(x => x is not null).ToList() : [];
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public async Task<bool> ExistsAsync(ImportJob job, IReadOnlyDictionary<string, string?> key, CancellationToken ct)
    {
        if (key.Count == 0) return false;
        var q = db.FormRecords.AsNoTracking().Where(r => r.TemplateId == job.TargetTemplateId);
        var candidates = await q.Select(r => r.ValuesJson).ToListAsync(ct);
        return candidates.Any(json =>
        {
            var v = JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? [];
            return key.All(k => string.Equals(v.GetValueOrDefault(k.Key)?.Trim(), k.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
        });
    }

    public async Task<Guid> CreateAsync(ImportJob job, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var (rev, def, doc) = await TemplateAsync(job, ct);
        var values = FormDefinitions.Apply(def, row.Where(k => k.Key != "__title").ToDictionary(), requireComplete: false);
        var title = row.GetValueOrDefault("__title");
        var rec = new FormRecord
        {
            Number = await numbers.NextAsync(RecordType, job.SiteId, ct), TemplateId = doc.Id, TemplateRevisionId = rev.Id, TemplateNumber = doc.Number, TemplateRevision = rev.Revision,
            Title = string.IsNullOrWhiteSpace(title) ? doc.Title : title.Trim()[..Math.Min(title.Trim().Length, 300)], SiteId = job.SiteId, DepartmentId = job.DepartmentId,
            Status = FormRecordStatus.Draft, ValuesJson = JsonSerializer.Serialize(values),
        };
        db.FormRecords.Add(rec);
        await db.SaveChangesAsync(ct);
        await indexer.IndexAsync(rec, values, ct);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, rec.Id, $"{doc.Number} rev {rev.Revision}", ct);
        return rec.Id;
    }

    public bool CanUpdate => true;

    public async Task<Guid?> FindAsync(ImportJob job, IReadOnlyDictionary<string, string?> key, CancellationToken ct)
    {
        if (key.Count == 0) return null;
        var candidates = await db.FormRecords.AsNoTracking().Where(r => r.TemplateId == job.TargetTemplateId && r.SiteId == job.SiteId).Select(r => new { r.Id, r.ValuesJson }).ToListAsync(ct);
        return candidates.FirstOrDefault(c =>
        {
            var v = JsonSerializer.Deserialize<Dictionary<string, string?>>(c.ValuesJson) ?? [];
            return key.All(k => string.Equals(v.GetValueOrDefault(k.Key)?.Trim(), k.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
        })?.Id;
    }

    public async Task<IReadOnlyDictionary<string, string?>> CurrentAsync(ImportJob job, Guid id, CancellationToken ct)
    {
        var rec = await db.FormRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec is null) return new Dictionary<string, string?>();
        var v = JsonSerializer.Deserialize<Dictionary<string, string?>>(rec.ValuesJson) ?? [];
        v["__title"] = rec.Title;
        return v;
    }

    /// <summary>Only draft records that nobody has signed are updated; the update is checked against the record's own template revision.</summary>
    public async Task<(string? Before, string? Refused)> UpdateAsync(ImportJob job, Guid id, IReadOnlyDictionary<string, string?> row, CancellationToken ct)
    {
        var rec = await db.FormRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec is null) return (null, "The record no longer exists.");
        if (rec.Status != FormRecordStatus.Draft) return (null, $"{rec.Number} has been submitted.");
        if (await db.FormSignatures.AnyAsync(s => s.FormRecordId == id, ct)) return (null, $"{rec.Number} has been signed.");
        var rev = await db.DocumentRevisions.AsNoTracking().FirstAsync(r => r.Id == rec.TemplateRevisionId, ct);
        var def = FormDefinitions.Parse(rev.FieldSchema!);
        var before = rec.ValuesJson;
        var merged = JsonSerializer.Deserialize<Dictionary<string, string?>>(rec.ValuesJson) ?? [];
        foreach (var (k, v) in row) if (k != "__title" && v is not null) merged[k] = v;
        var values = FormDefinitions.Apply(def, merged, requireComplete: false);
        rec.ValuesJson = JsonSerializer.Serialize(values);
        if (row.GetValueOrDefault("__title") is { Length: > 0 } t) rec.Title = t.Trim()[..Math.Min(t.Trim().Length, 300)];
        await db.SaveChangesAsync(ct);
        await indexer.IndexAsync(rec, values, ct);
        await db.SaveChangesAsync(ct);
        return (JsonSerializer.Serialize(new { values = before }), null);
    }

    public async Task<string?> RevertAsync(ImportJob job, Guid id, string before, CancellationToken ct)
    {
        var rec = await db.FormRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec is null) return "The record no longer exists.";
        if (rec.Status != FormRecordStatus.Draft) return $"{rec.Number} has been submitted since the import.";
        if (await db.FormSignatures.AnyAsync(s => s.FormRecordId == id, ct)) return $"{rec.Number} has been signed since the import.";
        var prev = JsonDocument.Parse(before).RootElement.GetProperty("values").GetString() ?? "{}";
        rec.ValuesJson = prev;
        await db.SaveChangesAsync(ct);
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(prev) ?? [];
        await indexer.IndexAsync(rec, values, ct);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> RollbackAsync(ImportJob job, Guid id, CancellationToken ct)
    {
        var rec = await db.FormRecords.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rec is null) return null;
        if (rec.Status != FormRecordStatus.Draft) return $"{rec.Number} has been submitted.";
        if (await db.FormSignatures.AnyAsync(s => s.FormRecordId == id, ct)) return $"{rec.Number} has been signed.";
        if (rec.UpdatedAt > rec.CreatedAt.AddSeconds(5)) return $"{rec.Number} was changed after the import.";
        await db.FormRecordValues.Where(v => v.FormRecordId == id).ExecuteDeleteAsync(ct);
        db.FormRecords.Remove(rec);
        await db.SaveChangesAsync(ct);
        await search.RemoveAsync(RecordType, id, ct);
        return null;
    }
}
