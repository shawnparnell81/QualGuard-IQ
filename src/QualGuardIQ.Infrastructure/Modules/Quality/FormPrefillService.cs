using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

/// <summary>Fill a draft form record from another record: <c>FormRecord</c> (for example a PPAP common data page or characteristics list) or <c>Capa</c>.</summary>
public sealed record PrefillRequest(string SourceType, Guid SourceId, bool Overwrite = false);
public sealed record PrefillResult(int Filled, List<string> Keys, Dictionary<string, string?> Values);
public sealed record StartFromCapaRequest(string TemplateKey = "capa-report");

/// <summary>
/// Copies shared data into a draft record so one entry feeds many forms: part, customer and supplier details from a PPAP common data page,
/// characteristic rows from a characteristics list into dimensional, material and performance results, and the details of a CAPA into the
/// CAPA report. A field takes the source value with its own key, or the key named by its <see cref="FormField.Prefill"/>; a table takes the
/// rows of the source table with the same key or its <see cref="FormTable.Prefill"/>, column by column. Empty targets are filled; existing
/// entries are kept unless overwrite is asked for. The record is then saved the normal way (validated, recalculated, signatures invalidated
/// when content changes) and one plain-language audit entry names the source and the number of entries copied.
/// </summary>
public sealed class FormPrefillService(AppDbContext db, ModuleSupport m, FormService forms, ICurrentUser current)
{
    /// <summary>Per-record entries that never copy: each record has its own number, title, status and dates.</summary>
    static readonly HashSet<string> OwnEntries = ["record_title", "reference_no", "record_status", "opened_on", "due_on", "closed_on", "supporting_files", "photos", "related_records"];

    public async Task<PrefillResult> PrefillAsync(Guid recordId, PrefillRequest req, CancellationToken ct)
    {
        await m.EditAsync(FormService.RecordType, recordId, ct);
        var (rec, def, values) = await forms.GetAsync(recordId, ct);
        if (rec.Status is not (FormRecordStatus.Draft or FormRecordStatus.Rejected)) throw new AppException("Only draft records can be filled.", 409);
        var (label, source, isCapa) = await SourceAsync(req, recordId, ct);
        var keys = Merge(def, values, source, req.Overwrite, isCapa);
        if (keys.Count == 0) return new PrefillResult(0, [], values);
        var saved = await forms.SaveAsync(recordId, values, rec.RowVersion, ct);
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAt = m.Now, UserId = current.UserId, UserName = current.UserName, Action = "Filled from record", EntityType = FormService.RecordType, EntityId = rec.Id, SiteId = rec.SiteId,
            Description = $"Filled {keys.Count} entr{(keys.Count == 1 ? "y" : "ies")} on form record {rec.Number} from {label}{(req.Overwrite ? ", replacing existing entries" : "")}",
            Changes = JsonSerializer.Serialize(keys), CorrelationId = current.CorrelationId,
        });
        await db.SaveChangesAsync(ct);
        return new PrefillResult(keys.Count, keys, saved);
    }

    /// <summary>Starts the CAPA report (or another installed library form) for a CAPA, filled with what the CAPA already records.</summary>
    public async Task<Guid> StartFromCapaAsync(Guid capaId, StartFromCapaRequest req, CancellationToken ct)
    {
        await m.ViewAsync("Capa", capaId, ct);
        var capa = await db.Capas.AsNoTracking().FirstAsync(c => c.Id == capaId, ct);
        var templateId = await db.TemplatePackageItems.AsNoTracking()
            .Where(i => i.TemplateKey == req.TemplateKey && i.DocumentId != null && db.Documents.Any(d => d.Id == i.DocumentId && d.Kind == DocumentKind.FormTemplate && !d.IsArchived && d.Revisions.Any(r => r.Status == RevisionStatus.Effective)))
            .OrderByDescending(i => i.InstalledAt).Select(i => i.DocumentId).FirstOrDefaultAsync(ct)
            ?? throw new AppException("The CAPA report form is not in use yet. Install an industry template pack and make its CAPA report effective first.");
        var rec = await forms.StartAsync(new StartFormRequest(templateId, capa.SiteId, capa.DepartmentId, $"CAPA {capa.Number} report"), ct);
        await PrefillAsync(rec.Id, new PrefillRequest("Capa", capaId), ct);
        return rec.Id;
    }

    async Task<(string Label, Source Source, bool IsCapa)> SourceAsync(PrefillRequest req, Guid recordId, CancellationToken ct)
    {
        switch (req.SourceType)
        {
            case FormService.RecordType:
                {
                    if (req.SourceId == recordId) throw new AppException("A record cannot be filled from itself.");
                    var (src, def, vals) = await forms.GetAsync(req.SourceId, ct);
                    return ($"form record {src.Number} ({src.Title})", new Source(vals, def.Tables.Select(t => t.Key).ToHashSet()), false);
                }
            case "Capa":
                {
                    await m.ViewAsync("Capa", req.SourceId, ct);
                    var c = await db.Capas.AsNoTracking().FirstAsync(x => x.Id == req.SourceId, ct);
                    return ($"CAPA {c.Number}", new Source(FromCapa(c), ["containment_actions"]), true);
                }
            default: throw new AppException("Records can be filled from a form record or a CAPA.");
        }
    }

    sealed record Source(Dictionary<string, string?> Values, HashSet<string> Tables);

    static List<string> Merge(FormDefinition def, Dictionary<string, string?> values, Source src, bool overwrite, bool fromCapa)
    {
        var keys = new List<string>();
        bool Empty(string k) => !values.TryGetValue(k, out var v) || string.IsNullOrWhiteSpace(v) || v is "[]" or "{\"rows\":[]}";
        foreach (var f in def.Fields)
        {
            if (f.Type is "calc" or "instruction" or "signature" || (!fromCapa && OwnEntries.Contains(f.Key))) continue;
            var from = f.Prefill ?? f.Key;
            if (!src.Values.TryGetValue(from, out var v) || string.IsNullOrWhiteSpace(v) || src.Tables.Contains(from)) continue;
            if (!overwrite && !Empty(f.Key)) continue;
            if (f.Options is { Count: > 0 } opts && f.Type == "choice" && !opts.Contains(v)) continue;
            values[f.Key] = v; keys.Add(f.Key);
        }
        foreach (var t in def.Tables)
        {
            if (!fromCapa && OwnEntries.Contains(t.Key)) continue;
            var from = t.Prefill ?? t.Key;
            if (!src.Tables.Contains(from) || !src.Values.TryGetValue(from, out var json) || json is null) continue;
            if (!overwrite && !Empty(t.Key)) continue;
            var rows = (JsonNode.Parse(json) is JsonObject o ? o["rows"] : JsonNode.Parse(json)) as JsonArray;
            if (rows is null || rows.Count == 0) continue;
            var copied = new JsonArray();
            foreach (var r in rows.OfType<JsonObject>())
            {
                var row = new JsonObject();
                foreach (var c in t.Columns.Where(c => c.Type is not "calc"))
                    if (r[c.Prefill ?? c.Key] is { } cell && cell.ToString() is { Length: > 0 }) row[c.Key] = cell.DeepClone();
                if (row.Count > 0) copied.Add(row);
            }
            if (copied.Count == 0) continue;
            values[t.Key] = new JsonObject { ["rows"] = copied }.ToJsonString();
            keys.Add(t.Key);
        }
        return keys;
    }

    /// <summary>What a CAPA already records, in the keys of the CAPA report.</summary>
    static Dictionary<string, string?> FromCapa(Capa c)
    {
        var status = c.Status switch
        {
            CapaStatus.Open => "Open", CapaStatus.Investigation => "Investigation", CapaStatus.Implementation => "Action in Progress",
            CapaStatus.EffectivenessCheck => "Effectiveness Monitoring", CapaStatus.Closed => "Closed", _ => "Cancelled",
        };
        var type = c.CapaType.StartsWith("Prev", StringComparison.OrdinalIgnoreCase) ? "Preventive Action" : c.CapaType.StartsWith("Both", StringComparison.OrdinalIgnoreCase) ? "Both" : "Corrective Action";
        var priority = c.Risk.ToString() switch { "Critical" => "Critical", "Major" or "High" => "High", "Minor" or "Low" => "Low", _ => "Medium" };
        var v = new Dictionary<string, string?>
        {
            ["reference_no"] = c.Number, ["record_title"] = c.Title, ["record_status"] = status, ["capa_type"] = type, ["priority_risk"] = priority,
            ["source_trigger"] = c.Source, ["source_reference"] = c.Source, ["site"] = c.SiteId.ToString(), ["department"] = c.DepartmentId?.ToString(), ["owner"] = c.OwnerId.ToString(),
            ["opened_on"] = c.CreatedAt.ToString("yyyy-MM-dd"), ["due_on"] = c.DueOn?.ToString("yyyy-MM-dd"), ["closed_on"] = c.ClosedAt?.ToString("yyyy-MM-dd"),
            ["problem_statement"] = c.ProblemStatement, ["scope_boundaries"] = c.Scope, ["impact_compliance"] = c.Impact,
            ["root_causes_confirmed"] = c.RootCause, ["contributing_causes"] = c.ContributingCauses,
            ["investigation_methods"] = c.RootCauseMethod is { Length: > 0 } rm ? JsonSerializer.Serialize(new[] { rm switch { "FiveWhys" or "5Whys" or "5 Whys" => "5 Whys", "Fishbone" or "Ishikawa" => "Fishbone", "IsIsNot" => "Is / Is Not", "FaultTree" => "Fault Tree", _ => "Other" } }) : null,
            ["effectiveness_criteria"] = c.EffectivenessCriteria, ["actual_results"] = c.EffectivenessResult, ["monitoring_method"] = c.RecurrenceMonitoring,
            ["effectiveness_result"] = c.WasEffective switch { true => "Effective", false => "Not Effective", _ => c.Status == CapaStatus.EffectivenessCheck ? "Pending" : null },
        };
        var cont = new[] { c.ImmediateCorrection, c.Containment }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => new JsonObject { ["cont_action"] = x }).ToArray();
        if (cont.Length > 0) v["containment_actions"] = new JsonObject { ["rows"] = new JsonArray(cont) }.ToJsonString();
        return v;
    }
}
