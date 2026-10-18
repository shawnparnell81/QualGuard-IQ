using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

/// <summary>
/// Writes a record's normalized values into form_record_values, one typed row per field (and per table row),
/// so records can be searched and reported on by field. ValuesJson stays the record of truth.
/// </summary>
public sealed class FormValueIndexer(AppDbContext db, FormDefinitionProjector projector)
{
    private readonly Dictionary<Guid, (List<TemplateFormField> Fields, Dictionary<Guid, string> Tables)> layouts = [];

    /// <param name="isNew">The record has just been created (bulk import): there are no earlier values to clear, and the revision's field layout is reused across rows.</param>
    public async Task IndexAsync(FormRecord rec, IReadOnlyDictionary<string, string?> values, CancellationToken ct, bool isNew = false)
    {
        List<TemplateFormField> fields; Dictionary<Guid, string> tables;
        if (isNew && layouts.TryGetValue(rec.TemplateRevisionId, out var cached)) (fields, tables) = cached;
        else
        {
            var defId = await EnsureDefinitionAsync(rec.TemplateRevisionId, ct);
            if (defId is null) return;
            fields = await db.TemplateFormFields.AsNoTracking().Where(f => f.DefinitionId == defId).ToListAsync(ct);
            tables = await db.TemplateFormTables.AsNoTracking().Where(t => t.DefinitionId == defId).ToDictionaryAsync(t => t.Id, t => t.Key, ct);
            if (isNew) layouts[rec.TemplateRevisionId] = (fields, tables);
        }
        if (!isNew) await db.FormRecordValues.Where(v => v.FormRecordId == rec.Id).ExecuteDeleteAsync(ct);
        var rows = new List<FormRecordValue>();
        foreach (var f in fields.Where(f => f.TableId is null && f.DataType != "none"))
            if (values.TryGetValue(f.Key, out var v) && !string.IsNullOrEmpty(v)) rows.Add(Typed(rec.Id, f, null, v));
        foreach (var group in fields.Where(f => f.TableId is not null).GroupBy(f => f.TableId!.Value))
        {
            if (!values.TryGetValue(tables[group.Key], out var json) || string.IsNullOrEmpty(json)) continue;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array) continue;
            var i = 0;
            foreach (var row in rowsEl.EnumerateArray())
            {
                var rowKey = i++.ToString(CultureInfo.InvariantCulture);
                foreach (var f in group)
                {
                    var cell = Find(row, f.Key);
                    if (cell is { } c && c.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                    {
                        var text = c.ValueKind == JsonValueKind.String ? c.GetString() : c.GetRawText();
                        if (!string.IsNullOrEmpty(text)) rows.Add(Typed(rec.Id, f, rowKey, text));
                    }
                }
            }
        }
        db.FormRecordValues.AddRange(rows);
    }

    private static JsonElement? Find(JsonElement row, string path)
    {
        if (row.ValueKind != JsonValueKind.Object) return null;
        if (row.TryGetProperty(path, out var direct)) return direct;
        var leaf = path[(path.LastIndexOf('.') + 1)..];
        return row.TryGetProperty(leaf, out var l) ? l : null;
    }

    private static FormRecordValue Typed(Guid recordId, TemplateFormField f, string? rowKey, string v)
    {
        var x = new FormRecordValue { FormRecordId = recordId, FieldId = f.Id, RowKey = rowKey };
        switch (f.DataType)
        {
            case "number" when decimal.TryParse(v, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var n):
                x.ValueType = "number"; x.NumericValue = n; break;
            case "date" or "datetime" when DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d):
                x.ValueType = "date"; x.DateValue = d.ToUniversalTime(); break;
            case "boolean" when bool.TryParse(v, out var b):
                x.ValueType = "boolean"; x.BooleanValue = b; break;
            case "file" when Guid.TryParse(v, out var g):
                x.ValueType = "text"; x.TextValue = g.ToString(); break;
            default:
                if (v.TrimStart() is ['[', ..] or ['{', ..] && IsJson(v)) { x.ValueType = "json"; x.JsonValue = v; }
                else { x.ValueType = "text"; x.TextValue = v; }
                break;
        }
        return x;
    }

    private static bool IsJson(string v) { try { JsonDocument.Parse(v).Dispose(); return true; } catch (JsonException) { return false; } }

    /// <summary>The projection of the record's template revision, backfilled (insert only) when the revision predates it.</summary>
    private async Task<Guid?> EnsureDefinitionAsync(Guid revisionId, CancellationToken ct)
    {
        var id = await db.TemplateFormDefinitions.Where(d => d.RevisionId == revisionId).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
        if (id is not null) return id;
        var rev = await db.DocumentRevisions.FirstAsync(r => r.Id == revisionId, ct);
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL qg.form_projection_backfill = 'on'", ct);
            if (rev.Status >= RevisionStatus.Approved) await projector.BackfillAsync(rev, ct); else await projector.SyncAsync(rev, ct);
            await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL qg.form_projection_backfill = 'off'", ct);
            if (own is not null) await own.CommitAsync(ct);
        }
        finally { if (own is not null) await own.DisposeAsync(); }
        return await db.TemplateFormDefinitions.Where(d => d.RevisionId == revisionId).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct);
    }
}
