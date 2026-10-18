using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Modules.Templates;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

/// <summary>
/// Form templates for integrations: a template with its revisions, each revision's form schema and formula definitions, and the formula
/// function catalog. The searchable catalog is <c>api/v1/template-library</c>; template packages are <c>api/v1/template-packages</c>.
/// </summary>
[ApiController, Authorize, Route("api/v1/form-templates"), Tags("Form Templates")]
public sealed class FormTemplatesController(AppDbContext db, IRecordRegistry records) : ControllerBase
{
    public sealed record RevisionInfo(Guid Id, string Revision, int Sequence, RevisionStatus Status, int SchemaVersion, int RenderingVersion, TemplateSource SourceType,
        DateOnly? EffectiveDate, DateTimeOffset? ApprovedAt, string ChangeSummary, int Records, uint RowVersion);
    public sealed record TemplateInfo(Guid Id, string Number, string Title, DocumentStatus Status, Guid SiteId, Guid? DepartmentId, string Confidentiality,
        string? ModuleKey, string? IndustryPack, TemplateSource Source, IReadOnlyList<RevisionInfo> Revisions);
    public sealed record SchemaInfo(Guid RevisionId, string Revision, RevisionStatus Status, int SchemaVersion, FormDefinition Definition);
    public sealed record FormulaInfo(string Kind, string Target, string? Label, string Formula, IReadOnlyList<string> References);
    public sealed record FunctionInfo(string Name, int MinArguments, int? MaxArguments);

    /// <summary>A form template and all of its revisions (newest first), with how many records were filled against each.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.FormsView)]
    public async Task<TemplateInfo> Get(Guid id, CancellationToken ct)
    {
        await records.RequireViewAsync(DocumentRecordTypes.FormTemplate, id, ct);
        var d = await db.Documents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Kind == DocumentKind.FormTemplate, ct) ?? throw new NotFoundException("Form template");
        var revs = await db.DocumentRevisions.AsNoTracking().Where(r => r.DocumentId == id).OrderByDescending(r => r.Sequence)
            .Select(r => new RevisionInfo(r.Id, r.Revision, r.Sequence, r.Status, r.SchemaVersion, r.RenderingVersion, r.SourceType, r.EffectiveDate, r.ApprovedAt, r.ChangeSummary,
                db.FormRecords.Count(f => f.TemplateRevisionId == r.Id), r.RowVersion)).ToListAsync(ct);
        return new TemplateInfo(d.Id, d.Number, d.Title, d.Status, d.SiteId, d.DepartmentId, d.Confidentiality, d.ModuleKey, d.IndustryPack, d.Source, revs);
    }

    /// <summary>The form schema of a revision, read as the current definition model (a version 1 field list is returned with its implicit section).</summary>
    [HttpGet("{id:guid}/revisions/{revisionId:guid}/schema")]
    [RequirePermission(Permissions.FormsView)]
    public async Task<SchemaInfo> Schema(Guid id, Guid revisionId, CancellationToken ct)
    {
        var r = await RevisionAsync(id, revisionId, ct);
        var def = FormDefinitions.Parse(r.FieldSchema ?? "[]");
        return new SchemaInfo(r.Id, r.Revision, r.Status, def.SchemaVersion, def);
    }

    /// <summary>Every formula in a revision: calculated fields, table column formulas, table totals and formatting rules, with the values each one uses.</summary>
    [HttpGet("{id:guid}/revisions/{revisionId:guid}/formulas")]
    [RequirePermission(Permissions.FormsView)]
    public async Task<IReadOnlyList<FormulaInfo>> Formulas(Guid id, Guid revisionId, CancellationToken ct)
    {
        var r = await RevisionAsync(id, revisionId, ct);
        var def = FormDefinitions.Parse(r.FieldSchema ?? "[]");
        var list = new List<FormulaInfo>();
        static IReadOnlyList<string> Refs(string f) => FormulaEngine.References(f).OrderBy(x => x, StringComparer.Ordinal).ToList();
        foreach (var f in def.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Formula))) list.Add(new("field", f.Key, f.Label, f.Formula!, Refs(f.Formula!)));
        foreach (var t in def.Tables)
        {
            foreach (var c in t.Columns.Where(c => !string.IsNullOrWhiteSpace(c.Formula))) list.Add(new("column", $"{t.Key}.{c.Key}", c.Label, c.Formula!, Refs(c.Formula!)));
            foreach (var tot in t.Totals ?? []) list.Add(new("total", $"{t.Key}.{tot.Column}", tot.Label, $"{tot.Function.ToUpperInvariant()}({t.Key}.{tot.Column})", [$"{t.Key}.{tot.Column}"]));
        }
        foreach (var (rule, i) in def.Rules.Select((x, i) => (x, i)).Where(x => !string.IsNullOrWhiteSpace(x.x.Formula)))
            list.Add(new("rule", rule.Target, $"Formatting rule {i + 1}", rule.Formula!, Refs(rule.Formula!)));
        return list;
    }

    /// <summary>The functions form formulas may use, with their argument counts (no maximum when null).</summary>
    [HttpGet("formula-functions")]
    [RequirePermission(Permissions.FormsView)]
    public IReadOnlyList<FunctionInfo> Functions() =>
        FormulaEngine.Functions.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new FunctionInfo(f.Key, f.Value.Min, f.Value.Max < 0 ? null : f.Value.Max)).ToList();

    private async Task<DocumentRevision> RevisionAsync(Guid id, Guid revisionId, CancellationToken ct)
    {
        await records.RequireViewAsync(DocumentRecordTypes.FormTemplate, id, ct);
        return await db.DocumentRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == revisionId && r.DocumentId == id && r.Document!.Kind == DocumentKind.FormTemplate, ct)
            ?? throw new NotFoundException("Revision");
    }
}

/// <summary>
/// Opt-in form schema migration wizard. Templates are never converted automatically: each migration keeps the original revision and a hashed
/// snapshot of it, creates a new draft revision with the converted definition and a field comparison, goes through the normal approval route and can be rolled back.
/// </summary>
[ApiController, Authorize, Route("api/v1/form-migrations"), Tags("Form Migrations")]
public sealed class FormMigrationsController(FormMigrationService migrations) : ControllerBase
{
    /// <summary>Effective templates on an earlier form schema version, with the records filled against them.</summary>
    [HttpGet("candidates")]
    [RequirePermission(Permissions.FormsView)]
    public Task<IReadOnlyList<MigrationCandidate>> Candidates(CancellationToken ct) => migrations.CandidatesAsync(ct);

    /// <summary>Migration results, newest first; optionally for one template.</summary>
    [HttpGet]
    [RequirePermission(Permissions.FormsView)]
    public Task<IReadOnlyList<MigrationSummary>> List([FromQuery] Guid? documentId, CancellationToken ct) => migrations.ListAsync(documentId, ct);

    /// <summary>One migration with its field comparison, snapshot hash check and the actions available now.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.FormsView)]
    public Task<MigrationDetail> Get(Guid id, CancellationToken ct) => migrations.GetAsync(id, ct);

    /// <summary>The migration snapshot: the original definition exactly as stored, its hash and the record count at migration time.</summary>
    [HttpGet("{id:guid}/snapshot")]
    [RequirePermission(Permissions.FormsView)]
    public Task<System.Text.Json.Nodes.JsonNode?> Snapshot(Guid id, CancellationToken ct) => migrations.SnapshotAsync(id, ct);

    /// <summary>Migrate a template: snapshot, new draft revision with the converted definition, and the comparison. Nothing becomes effective until approved.</summary>
    [EnableRateLimiting("processing")]
    [HttpPost]
    [RequirePermission(Permissions.FormsEdit)]
    public Task<MigrationDetail> Start(StartMigrationRequest req, CancellationToken ct) => migrations.StartAsync(req, ct);

    /// <summary>Send the migrated revision through the template's approval route.</summary>
    [HttpPost("{id:guid}/submit")]
    [RequirePermission(Permissions.FormsEdit)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await migrations.SubmitAsync(id, ct); return NoContent(); }

    /// <summary>Roll a migration back to the original definition (a reason is required). Records keep the revision they were filled against.</summary>
    [EnableRateLimiting("processing")]
    [HttpPost("{id:guid}/rollback")]
    [RequirePermission(Permissions.FormsApprove)]
    public Task<MigrationDetail> Rollback(Guid id, RollbackMigrationRequest req, CancellationToken ct) => migrations.RollbackAsync(id, req, ct);
}
