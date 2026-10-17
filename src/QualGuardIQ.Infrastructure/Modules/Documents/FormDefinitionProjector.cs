using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Documents;

/// <summary>
/// Derives the relational form definition (definition, sections, tables, fields) from a template revision's FieldSchema.
/// The FieldSchema stays the single source of truth; the projection is rebuilt whenever a draft changes and is frozen
/// with the revision once it is approved (code lock plus database trigger).
/// </summary>
public sealed class FormDefinitionProjector(AppDbContext db)
{
    public static string Hash(string schema) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema))).ToLowerInvariant();

    public static string DataTypeOf(string type) => type switch
    {
        "number" or "integer" or "currency" or "percent" or "measurement" or "calc" => "number",
        "date" => "date",
        "datetime" => "datetime",
        "time" => "time",
        "boolean" => "boolean",
        "choice" or "radio" or "multichoice" or "passfail" => "list",
        "attachment" or "photo" => "file",
        "instruction" => "none",
        _ when FormDefinitions.ReferenceTypes.Contains(type) => "reference",
        _ => "text"
    };

    /// <summary>Rebuilds the projection of a draft revision. Does not save; the caller's SaveChanges commits it with the revision.</summary>
    public async Task SyncAsync(DocumentRevision rev, CancellationToken ct)
    {
        if (rev.Status >= RevisionStatus.Approved) throw new InvalidOperationException("The form definition of a locked revision cannot change.");
        var existing = await db.TemplateFormDefinitions.FirstOrDefaultAsync(d => d.RevisionId == rev.Id, ct);
        if (string.IsNullOrWhiteSpace(rev.FieldSchema))
        {
            if (existing is not null) await RemoveAsync(existing, ct);
            return;
        }
        var hash = Hash(rev.FieldSchema);
        if (existing?.SchemaHash == hash) return;
        if (existing is not null) await RemoveAsync(existing, ct);
        var def = FormDefinitions.Parse(rev.FieldSchema);
        rev.SchemaVersion = def.SchemaVersion;
        Build(rev.Id, def, hash);
    }

    /// <summary>Adds the projection of a revision that has none yet (used for revisions approved before the projection existed).</summary>
    public async Task<bool> BackfillAsync(DocumentRevision rev, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rev.FieldSchema) || await db.TemplateFormDefinitions.AnyAsync(d => d.RevisionId == rev.Id, ct)) return false;
        Build(rev.Id, FormDefinitions.Parse(rev.FieldSchema), Hash(rev.FieldSchema));
        return true;
    }

    private async Task RemoveAsync(TemplateFormDefinition d, CancellationToken ct)
    {
        db.TemplateFormFields.RemoveRange(await db.TemplateFormFields.Where(x => x.DefinitionId == d.Id).ToListAsync(ct));
        db.TemplateFormTables.RemoveRange(await db.TemplateFormTables.Where(x => x.DefinitionId == d.Id).ToListAsync(ct));
        db.TemplateFormSections.RemoveRange(await db.TemplateFormSections.Where(x => x.DefinitionId == d.Id).ToListAsync(ct));
        db.TemplateFormDefinitions.Remove(d);
        await db.SaveChangesAsync(ct);
    }

    private static string J(object? o) => JsonSerializer.Serialize(o, FormDefinitions.Json);

    private void Build(Guid revisionId, FormDefinition def, string hash)
    {
        var allFields = def.Fields.Concat(def.Tables.SelectMany(t => Flatten(t.Columns))).ToList();
        var d = new TemplateFormDefinition
        {
            Id = Guid.NewGuid(),
            RevisionId = revisionId,
            SchemaHash = hash,
            LayoutSchema = def.Layout is null ? "{}" : J(def.Layout),
            ValidationSchema = J(allFields.Where(f => f.Required || f.Min is not null || f.Max is not null || f.Pattern is not null || f.Checks is { Count: > 0 } || f.RequiredWhen is not null)
                .GroupBy(f => f.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(f => new { f.Required, f.Min, f.Max, f.Pattern, f.PatternMessage, f.Checks, f.RequiredWhen }).ToList(), StringComparer.Ordinal)),
            FormulaSchema = J(new
            {
                fields = def.Fields.Where(f => f.Formula is not null).ToDictionary(f => f.Key, f => f.Formula),
                tables = def.Tables.ToDictionary(t => t.Key, t => new { columns = Flatten(t.Columns).Where(c => c.Formula is not null).ToDictionary(c => c.Key, c => c.Formula), totals = t.Totals, runningTotals = t.RunningTotals, subtotalBy = t.SubtotalBy })
            }),
            WorkflowConfiguration = J(new { signatures = def.Signatures }),
            PrintConfiguration = J(def.Page),
            SearchConfiguration = J(new { fields = def.Fields.Where(f => DataTypeOf(f.Type) is "text" or "reference" or "list").Select(f => f.Key) }),
            ReportingConfiguration = J(new { measures = allFields.Where(f => DataTypeOf(f.Type) == "number").Select(f => f.Key), dimensions = allFields.Where(f => DataTypeOf(f.Type) is "list" or "reference" or "date").Select(f => f.Key) })
        };
        db.TemplateFormDefinitions.Add(d);

        var sections = new Dictionary<string, TemplateFormSection>(StringComparer.Ordinal);
        for (var i = 0; i < def.Sections.Count; i++)
        {
            var s = def.Sections[i];
            sections[s.Key] = new TemplateFormSection
            {
                Id = Guid.NewGuid(), DefinitionId = d.Id, Key = s.Key, Sequence = i, Title = s.Title, Instructions = s.Instructions, IsRepeating = s.Repeatable,
                Layout = J(new { s.Columns, s.Orientation, s.PageBreakBefore, s.KeepTogether, s.MinRepeat, s.MaxRepeat, s.RepeatLabel }),
                ConditionalRule = s.VisibleWhen is null ? null : J(s.VisibleWhen)
            };
        }
        foreach (var s in def.Sections)
            if (s.Parent is { } p && sections.TryGetValue(p, out var parent)) sections[s.Key].ParentSectionId = parent.Id;
        db.TemplateFormSections.AddRange(sections.Values);
        Guid? SectionOf(string? key) => key is not null && sections.TryGetValue(key, out var x) ? x.Id : null;

        for (var i = 0; i < def.Fields.Count; i++) db.TemplateFormFields.Add(Field(d.Id, SectionOf(def.Fields[i].Section), null, def.Fields[i], i, null));
        for (var i = 0; i < def.Tables.Count; i++)
        {
            var t = def.Tables[i];
            var table = new TemplateFormTable
            {
                Id = Guid.NewGuid(), DefinitionId = d.Id, SectionId = SectionOf(t.Section), Key = t.Key, Name = t.Title, Sequence = i,
                HeaderRows = t.HeaderGroups is { Count: > 0 } ? 2 : 1, DataRows = t.FixedRows?.Count, IsRepeating = t.FixedRows is null,
                ColumnDefinitions = J(new { columns = t.Columns, headerGroups = t.HeaderGroups, fixedRows = t.FixedRows, t.MinRows, t.MaxRows, t.RepeatHeaderOnEachPage }),
                FormulaRules = J(Flatten(t.Columns).Where(c => c.Formula is not null).Select(c => new { column = c.Key, formula = c.Formula })),
                SubtotalRules = J(t.SubtotalBy is null ? Array.Empty<object>() : [new { by = t.SubtotalBy, runningTotals = t.RunningTotals }]),
                TotalRules = J(t.Totals ?? [])
            };
            db.TemplateFormTables.Add(table);
            var seq = 0;
            foreach (var (c, path) in FlattenWithPath(t.Columns, ""))
                db.TemplateFormFields.Add(Field(d.Id, table.SectionId, table.Id, c, seq++, path));
        }
    }

    private static TemplateFormField Field(Guid defId, Guid? sectionId, Guid? tableId, FormField f, int seq, string? path) => new()
    {
        Id = Guid.NewGuid(), DefinitionId = defId, SectionId = sectionId, TableId = tableId,
        Key = path ?? f.Key, Label = f.Label, FieldType = f.Type, DataType = DataTypeOf(f.Type), Sequence = seq, IsRequired = f.Required,
        ValidationRule = f.Min is null && f.Max is null && f.Pattern is null && f.Checks is null ? null : J(new { f.Min, f.Max, f.Pattern, f.PatternMessage, f.Checks }),
        ConditionalRule = f.VisibleWhen is null && f.RequiredWhen is null ? null : J(new { f.VisibleWhen, f.RequiredWhen }),
        Formula = f.Formula,
        Format = J(new { f.Unit, f.Decimals, f.Width, f.MaxLength, f.Nominal, f.Options, f.ResultLabels, f.Accept, f.Placeholder, f.Help }),
        SourceLocation = f.SourceLocation,
        MappingConfidence = f.SourceLocation is null ? MappingConfidence.Manual : MappingConfidence.High,
        ManualReviewStatus = ManualReviewStatus.NotRequired
    };

    private static IEnumerable<FormField> Flatten(IEnumerable<FormField> cols) => FlattenWithPath(cols, "").Select(x => x.Field);

    /// <summary>Leaf columns; nested column keys are joined with a dot so they stay unique within the table.</summary>
    private static IEnumerable<(FormField Field, string Path)> FlattenWithPath(IEnumerable<FormField> cols, string prefix)
    {
        foreach (var c in cols)
        {
            var path = prefix + c.Key;
            if (c.Columns is { Count: > 0 }) foreach (var x in FlattenWithPath(c.Columns, path + ".")) yield return x;
            else yield return (c, path);
        }
    }
}
