using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);
public sealed record TextRequest(string Text);

internal static class Allowed
{
    /// <summary>Names of the state transitions the current user can perform now; the UI shows only these buttons.</summary>
    public static List<string> Of(params (string Name, bool Ok)[] items) => items.Where(i => i.Ok).Select(i => i.Name).ToList();

    public static async Task<bool> ActionsClosedAsync(AppDbContext db, string type, Guid id, bool requireOne, CancellationToken ct)
    {
        var open = await db.ActionItems.AnyAsync(a => a.SourceType == type && a.SourceId == id && a.Status != QualGuardIQ.Domain.Shared.ActionStatus.Cancelled && a.Status != QualGuardIQ.Domain.Shared.ActionStatus.Verified
            && !(a.Status == QualGuardIQ.Domain.Shared.ActionStatus.Completed && !a.RequiresVerification), ct);
        if (open) return false;
        return !requireOne || await db.ActionItems.AnyAsync(a => a.SourceType == type && a.SourceId == id && a.Status != QualGuardIQ.Domain.Shared.ActionStatus.Cancelled, ct);
    }
}

internal static class Paging
{
    public static async Task<PagedResult<T>> PageAsync<T>(IQueryable<T> q, int page, int size, CancellationToken ct)
    {
        size = Math.Clamp(size, 1, 200);
        page = Math.Max(1, page);
        return new(await q.Skip((page - 1) * size).Take(size).ToListAsync(ct), await q.CountAsync(ct));
    }
}

[ApiController, Authorize, Route("api/forms")]
public sealed class FormsController(AppDbContext db, FormService forms, ModuleSupport m) : ControllerBase
{
    public sealed record SaveValues(Dictionary<string, string?> Values, uint RowVersion);

    [HttpGet("templates/effective")]
    [RequirePermission(Permissions.FormsView)]
    public async Task<object> EffectiveTemplates(CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.Documents.AsNoTracking().Where(d => d.Kind == DocumentKind.FormTemplate && !d.IsArchived && d.Revisions.Any(r => r.Status == RevisionStatus.Effective)), Permissions.FormsView, ct);
        return await q.OrderBy(d => d.Number).Select(d => new { d.Id, d.Number, d.Title, d.SiteId, Revision = d.Revisions.Where(r => r.Status == RevisionStatus.Effective).Select(r => r.Revision).FirstOrDefault() }).ToListAsync(ct);
    }

    [HttpGet("records")]
    [RequirePermission(Permissions.FormsView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] FormRecordStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.FormRecords.AsNoTracking(), Permissions.FormsView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + x.TemplateNumber, $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Title, x.TemplateNumber, x.TemplateRevision, x.Status, x.CreatedAt, x.SubmittedAt,
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First()
        }), page, pageSize, ct);
    }

    [HttpPost("records")]
    [RequirePermission(Permissions.FormsEdit)]
    public async Task<object> Start(StartFormRequest req, CancellationToken ct) => new { (await forms.StartAsync(req, ct)).Id };

    [HttpGet("records/{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        var (r, def, values) = await forms.GetAsync(id, ct);
        var editable = r.Status is FormRecordStatus.Draft or FormRecordStatus.Rejected && await m.CanAsync(Permissions.FormsEdit, r.SiteId, r.DepartmentId, ct);
        return new { r.Id, r.Number, r.Title, r.TemplateId, r.TemplateNumber, r.TemplateRevision, r.Status, r.SiteId, r.SubmittedAt, r.RowVersion, Fields = def.Fields, Definition = def, Values = values, CanEdit = editable,
            Signatures = (await forms.SignaturesAsync(id, ct)).Select(s => new { s.Id, Key = s.SignatureKey, s.Label, s.Role, s.Meaning, s.SignerId, s.SignerName, s.SignedAt, s.Verification, s.Comment, Valid = !s.IsInvalidated, s.InvalidatedAt, s.InvalidatedReason }) };
    }

    /// <summary>Form record as PDF: style=standardized (QualGuard IQ layout) or original (converted source layout). Printing is audited.</summary>
    [EnableRateLimiting("processing")]
    [HttpGet("records/{id:guid}/pdf")]
    public async Task<IActionResult> RecordPdf(Guid id, [FromQuery] string? style, [FromServices] FormPrintService print, CancellationToken ct)
    {
        var r = await print.RecordAsync(id, style, ct);
        Response.Headers["X-Print-Style"] = r.Style;
        return File(r.Pdf, "application/pdf", r.FileName);
    }

    /// <summary>Blank form template revision as PDF (controlled copy when effective, watermarked otherwise). Printing is audited.</summary>
    [EnableRateLimiting("processing")]
    [HttpGet("templates/revisions/{revisionId:guid}/pdf")]
    public async Task<IActionResult> TemplatePdf(Guid revisionId, [FromQuery] string? style, [FromServices] FormPrintService print, CancellationToken ct)
    {
        var r = await print.TemplateAsync(revisionId, style, ct);
        Response.Headers["X-Print-Style"] = r.Style;
        return File(r.Pdf, "application/pdf", r.FileName);
    }

    /// <summary>Protected workbook of the template's effective revision for controlled offline completion (Excel round trip). Audited.</summary>
    [HttpGet("templates/{templateId:guid}/workbook")]
    public async Task<IActionResult> OfflineWorkbook(Guid templateId, [FromServices] FormWorkbookService books, CancellationToken ct)
    {
        var (bytes, name) = await books.ExportAsync(templateId, ct);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    /// <summary>Reimports a completed offline workbook as a draft form record (integrity, revision and structure checked; input cells only; recalculated; kept as evidence; one submission per copy).</summary>
    [EnableRateLimiting("processing")]
    [HttpPost("templates/{templateId:guid}/workbook")]
    [RequestSizeLimit(55L * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 55L * 1024 * 1024)]
    public async Task<FormWorkbookService.Submission> SubmitOfflineWorkbook(Guid templateId, IFormFile file, [FromForm] Guid siteId, [FromForm] Guid? departmentId, [FromServices] FormWorkbookService books, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await books.ImportAsync(templateId, s, file.FileName, siteId, departmentId, ct);
    }

    public sealed record PreviewRequest(string FieldSchema, string? Style, Dictionary<string, string?>? Values);

    /// <summary>Designer print preview of an unsaved definition (validated first). Returns a PDF watermarked PREVIEW.</summary>
    [EnableRateLimiting("processing")]
    [HttpPost("preview/pdf")]
    [RequirePermission(Permissions.FormsEdit)]
    public async Task<IActionResult> PreviewPdf(PreviewRequest req, [FromServices] FormPrintService print, CancellationToken ct)
    {
        var r = await print.PreviewAsync(req.FieldSchema, req.Style, req.Values, ct);
        Response.Headers["X-Print-Style"] = r.Style;
        return File(r.Pdf, "application/pdf", r.FileName);
    }

    /// <summary>Designer check: validates an unsaved definition and computes calculated values for sample entries, without saving anything.</summary>
    [HttpPost("preview/evaluate")]
    [RequirePermission(Permissions.FormsEdit)]
    public object Evaluate(PreviewRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FieldSchema) || req.FieldSchema.Length > 4_000_000) throw new AppException("The form definition is missing or too large.");
        var def = FormDefinitions.Parse(req.FieldSchema);
        FormDefinitions.Validate(def);
        var values = FormDefinitions.Apply(def, req.Values ?? [], requireComplete: false);
        return new { Values = values, RuleHits = FormDefinitions.RuleHits(def, values), Fields = def.Fields.Count, Tables = def.Tables.Count, Sections = def.Sections.Count, Signatures = def.Signatures.Count };
    }

    [HttpPost("records/{id:guid}/signatures/{key}")]
    public async Task<object> Sign(Guid id, string key, SignFormRequest req, CancellationToken ct)
    {
        var s = await forms.SignAsync(id, key, req, ct);
        return new { s.Id, Key = s.SignatureKey, s.SignerName, s.SignedAt, s.Verification };
    }

    [HttpPut("records/{id:guid}")]
    public async Task<object> Save(Guid id, SaveValues req, CancellationToken ct) => new { Values = await forms.SaveAsync(id, req.Values, req.RowVersion, ct) };

    [HttpPost("records/{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await forms.SubmitAsync(id, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/ncrs")]
public sealed class NcrsController(AppDbContext db, NcrService ncrs, CapaService capas, ModuleSupport m) : ControllerBase
{
    public sealed record CreateCapaFromNcr(string? Title, string? ProblemStatement, Guid? OwnerId);

    [HttpGet]
    [RequirePermission(Permissions.NcrView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] NcrStatus? status, [FromQuery] bool openOnly = false, [FromQuery] Guid? supplierId = null, [FromQuery] Guid? customerId = null, [FromQuery] Guid? partId = null, [FromQuery] Guid? productId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        if (supplierId is not null) query = query.Where(x => x.SupplierId == supplierId);
        if (customerId is not null) query = query.Where(x => x.CustomerId == customerId);
        if (partId is not null) query = query.Where(x => x.PartId == partId);
        if (productId is not null) query = query.Where(x => x.ProductId == productId);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (openOnly) query = query.Where(x => x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + (x.PartNumber ?? "") + " " + (x.LotNumber ?? "") + " " + (x.BatchNumber ?? "") + " " + (x.SerialNumber ?? "") + " " + (x.WorkOrder ?? "") + " " + (x.PurchaseOrder ?? ""), $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Title, x.Source, x.Severity, x.Status, x.PartNumber, x.LotNumber, x.DetectedOn, x.Disposition,
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First(),
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    [HttpPost]
    [RequirePermission(Permissions.NcrEdit)]
    public async Task<object> Create(CreateNcrRequest req, CancellationToken ct) => new { (await ncrs.CreateAsync(req, ct)).Id };

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(NcrService.RecordType, id, ct);
        var n = await db.Ncrs.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var capaIds = await db.RecordLinks.Where(l => l.FromType == NcrService.RecordType && l.FromId == id && l.ToType == CapaService.RecordType).Select(l => l.ToId).ToListAsync(ct);
        var linkedCapas = await db.Capas.Where(c => capaIds.Contains(c.Id)).Select(c => new { c.Id, c.Number, c.Status }).ToListAsync(ct);
        var canEdit = await m.CanAsync(Permissions.NcrEdit, n.SiteId, n.DepartmentId, ct);
        var anyCapa = linkedCapas.Count > 0 || await db.RecordLinks.AnyAsync(l => l.ToType == NcrService.RecordType && l.ToId == id && l.FromType == CapaService.RecordType, ct);
        var s = n.Status;
        var allowed = Allowed.Of(("contain", canEdit && s <= NcrStatus.Contained), ("dispose", canEdit && s == NcrStatus.Contained),
            ("capa", canEdit && s is >= NcrStatus.Contained and <= NcrStatus.DispositionApproved),
            ("details", canEdit && s is NcrStatus.Open or NcrStatus.Contained or NcrStatus.DispositionApproved),
            ("verify", canEdit && s == NcrStatus.DispositionApproved && n.VerifiedAt == null),
            ("close", canEdit && s == NcrStatus.DispositionApproved && n.VerifiedAt != null && (!n.CapaRequired || anyCapa) && await Allowed.ActionsClosedAsync(db, NcrService.RecordType, id, false, ct)),
            ("reopen", s == NcrStatus.Closed && await m.CanAsync(Permissions.NcrApprove, n.SiteId, n.DepartmentId, ct)),
            ("actions", canEdit && s < NcrStatus.Closed));
        return new
        {
            n.Id, n.Number, n.Title, n.Description, n.Source, n.SiteId, n.DepartmentId, n.OwnerId, n.PartNumber, n.LotNumber, n.QuantityAffected, n.DetectedOn, n.DueOn, n.Severity,
            n.Status, n.ContainmentSummary, n.Disposition, n.DispositionJustification, n.CapaRequired, n.ClosureNotes, n.ClosedAt, n.CreatedAt,
            n.ProductId, n.PartId, n.PartRevision, n.SupplierId, n.CustomerId, n.Process, n.Operation, n.WorkOrder, n.PurchaseOrder, n.BatchNumber, n.SerialNumber,
            n.DetectionPoint, n.DefectCode, n.Requirement, n.ActualCondition, n.Segregation, n.Investigation, n.DispositionInstructions, n.QuantityDispositioned,
            n.QuantityScrapped, n.CostOfPoorQuality, n.VerificationNotes, n.VerifiedAt, n.ReopenCount, n.ReopenReason, n.RowVersion,
            VerifiedByName = n.VerifiedById == null ? null : await db.Users.Where(u => u.Id == n.VerifiedById).Select(u => u.DisplayName).FirstAsync(ct),
            ProductLabel = n.ProductId == null ? null : await db.Products.Where(x => x.Id == n.ProductId).Select(x => x.Number + " " + x.Name).FirstAsync(ct),
            PartLabel = n.PartId == null ? null : await db.Parts.Where(x => x.Id == n.PartId).Select(x => x.PartNumber + " " + x.Name).FirstAsync(ct),
            SupplierLabel = n.SupplierId == null ? null : await db.Suppliers.Where(x => x.Id == n.SupplierId).Select(x => x.Code + " " + x.LegalName).FirstAsync(ct),
            CustomerLabel = n.CustomerId == null ? null : await db.Customers.Where(x => x.Id == n.CustomerId).Select(x => x.Code + " " + x.Name).FirstAsync(ct),
            OwnerName = await db.Users.Where(u => u.Id == n.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(s => s.Id == n.SiteId).Select(s => s.Code).FirstAsync(ct),
            Capas = linkedCapas, CanEdit = canEdit, Allowed = allowed
        };
    }

    [HttpPost("{id:guid}/containment")]
    public async Task<IActionResult> Contain(Guid id, TextRequest r, CancellationToken ct) { await ncrs.ContainAsync(id, r.Text, ct); return NoContent(); }

    [HttpPost("{id:guid}/disposition")]
    public async Task<IActionResult> Disposition(Guid id, DispositionRequest r, CancellationToken ct) { await ncrs.ProposeDispositionAsync(id, r, ct); return NoContent(); }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, TextRequest r, CancellationToken ct) { await ncrs.CloseAsync(id, r.Text, ct); return NoContent(); }

    public sealed record DetailsRequest(NcrDetails Details, uint RowVersion);

    [HttpPut("{id:guid}/details")]
    public async Task<IActionResult> Details(Guid id, DetailsRequest r, CancellationToken ct) { await ncrs.UpdateDetailsAsync(id, r.Details, r.RowVersion, ct); return NoContent(); }

    [HttpPost("{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, NcrVerifyRequest r, CancellationToken ct) { await ncrs.VerifyAsync(id, r, ct); return NoContent(); }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, TextRequest r, CancellationToken ct) { await ncrs.ReopenAsync(id, r.Text, ct); return NoContent(); }

    [HttpPost("{id:guid}/capa")]
    public async Task<object> CreateCapa(Guid id, CreateCapaFromNcr r, CancellationToken ct)
    {
        await m.EditAsync(NcrService.RecordType, id, ct);
        var n = await db.Ncrs.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var c = await capas.CreateAsync(new CreateCapaRequest(string.IsNullOrWhiteSpace(r.Title) ? n.Title : r.Title, string.IsNullOrWhiteSpace(r.ProblemStatement) ? n.Description : r.ProblemStatement,
            "Corrective", "NCR", n.SiteId, n.DepartmentId, r.OwnerId), ct, (NcrService.RecordType, n.Id));
        return new { c.Id, c.Number };
    }
}

[ApiController, Authorize, Route("api/capas")]
public sealed class CapasController(AppDbContext db, CapaService capas, ModuleSupport m) : ControllerBase
{
    public sealed record VerifyRequest(bool Effective, string Result);

    [HttpGet]
    [RequirePermission(Permissions.CapaView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] CapaStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Capas.AsNoTracking(), Permissions.CapaView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title, $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Title, x.CapaType, x.Source, x.Status, x.EffectivenessDueOn, x.CreatedAt,
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First(),
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    [HttpPost]
    [RequirePermission(Permissions.CapaEdit)]
    public async Task<object> Create(CreateCapaRequest req, CancellationToken ct) => new { (await capas.CreateAsync(req, ct)).Id };

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(CapaService.RecordType, id, ct);
        var c = await db.Capas.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.CapaEdit, c.SiteId, c.DepartmentId, ct);
        var canVerify = c.OwnerId != m.UserId && await m.CanAsync(Permissions.CapaApprove, c.SiteId, c.DepartmentId, ct);
        var allowed = Allowed.Of(("rootCause", canEdit && c.Status <= CapaStatus.Implementation),
            ("check", canEdit && c.Status == CapaStatus.Implementation && await Allowed.ActionsClosedAsync(db, CapaService.RecordType, id, true, ct)),
            ("verify", canVerify && c.Status == CapaStatus.EffectivenessCheck), ("actions", canEdit && c.Status < CapaStatus.EffectivenessCheck),
            ("plan", canEdit && c.Status < CapaStatus.Closed),
            ("reopen", c.Status == CapaStatus.Closed && await m.CanAsync(Permissions.CapaApprove, c.SiteId, c.DepartmentId, ct)));
        var team = System.Text.Json.JsonSerializer.Deserialize<List<Guid>>(c.TeamJson) ?? [];
        return new
        {
            c.Id, c.Number, c.Title, c.ProblemStatement, c.CapaType, c.Source, c.SiteId, c.DepartmentId, c.OwnerId, c.Status, c.RootCauseMethod, c.RootCause,
            c.EffectivenessCriteria, c.EffectivenessDueOn, c.EffectivenessResult, c.WasEffective, c.ClosedAt, c.CreatedAt, c.DueOn,
            c.Scope, c.Impact, c.Risk, c.ImmediateCorrection, c.Containment, c.ContributingCauses, c.EscapePoint, c.RecurrenceMonitoring, c.RecurrenceReviewOn,
            c.ReopenCount, c.ReopenReason, c.RowVersion, Analysis = System.Text.Json.JsonDocument.Parse(c.AnalysisJson).RootElement.Clone(),
            Team = await db.Users.Where(u => team.Contains(u.Id)).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct),
            OwnerName = await db.Users.Where(u => u.Id == c.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            VerifiedByName = c.EffectivenessVerifiedById == null ? null : await db.Users.Where(u => u.Id == c.EffectivenessVerifiedById).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(s => s.Id == c.SiteId).Select(s => s.Code).FirstAsync(ct),
            CanEdit = canEdit, CanVerify = canVerify, Allowed = allowed
        };
    }

    [HttpPost("{id:guid}/root-cause")]
    public async Task<IActionResult> RootCause(Guid id, RootCauseRequest r, CancellationToken ct) { await capas.RecordRootCauseAsync(id, r, ct); return NoContent(); }

    [HttpPut("{id:guid}/plan")]
    public async Task<IActionResult> Plan(Guid id, CapaPlanRequest r, CancellationToken ct) { await capas.SavePlanAsync(id, r, ct); return NoContent(); }

    [HttpPost("{id:guid}/reopen")]
    public async Task<IActionResult> Reopen(Guid id, TextRequest r, CancellationToken ct) { await capas.ReopenAsync(id, r.Text, ct); return NoContent(); }

    [HttpPost("{id:guid}/effectiveness-check")]
    public async Task<IActionResult> StartCheck(Guid id, CancellationToken ct) { await capas.StartEffectivenessCheckAsync(id, ct); return NoContent(); }

    [HttpPost("{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, VerifyRequest r, CancellationToken ct) { await capas.VerifyEffectivenessAsync(id, r.Effective, r.Result, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/audits")]
public sealed class AuditsController(AppDbContext db, AuditService audits, ModuleSupport m) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.AuditsView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] AuditStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Audits.AsNoTracking(), Permissions.AuditsView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title, $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderByDescending(x => x.PlannedStart).Select(x => new
        {
            x.Id, x.Number, x.Title, x.AuditType, x.Status, x.PlannedStart, x.PlannedEnd,
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First(),
            LeadName = db.Users.Where(u => u.Id == x.LeadAuditorId).Select(u => u.DisplayName).First(),
            Findings = db.AuditFindings.Count(f => f.AuditId == x.Id)
        }), page, pageSize, ct);
    }

    [HttpGet("findings")]
    [RequirePermission(Permissions.AuditsView)]
    public async Task<object> Findings([FromQuery] bool openOnly = true, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.AuditFindings.AsNoTracking(), Permissions.AuditsView, ct);
        if (openOnly) query = query.Where(x => x.Status == FindingStatus.Open || x.Status == FindingStatus.ActionPlanned);
        return await Paging.PageAsync(query.OrderBy(x => x.ResponseDueOn).Select(x => new
        {
            x.Id, x.Number, x.AuditId, AuditNumber = db.Audits.Where(a => a.Id == x.AuditId).Select(a => a.Number).First(), x.FindingType, x.Clause, x.Statement, x.Status, x.ResponseDueOn,
            ResponsibleName = db.Users.Where(u => u.Id == x.ResponsibleId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    [HttpPost]
    [RequirePermission(Permissions.AuditsEdit)]
    public async Task<object> Create(CreateAuditRequest req, CancellationToken ct) => new { (await audits.CreateAsync(req, ct)).Id };

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        var (a, findings) = await audits.GetAsync(id, ct);
        var users = findings.Select(f => f.ResponsibleId).Append(a.LeadAuditorId).Distinct().ToList();
        var names = await db.Users.Where(u => users.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var canEdit = await m.CanAsync(Permissions.AuditsEdit, a.SiteId, a.DepartmentId, ct);
        var canClose = await m.CanAsync(Permissions.AuditsApprove, a.SiteId, a.DepartmentId, ct);
        var allowed = Allowed.Of(("start", canEdit && a.Status == AuditStatus.Planned), ("checklist", canEdit && a.Status == AuditStatus.InProgress),
            ("finding", canEdit && a.Status is AuditStatus.InProgress or AuditStatus.Reporting),
            ("report", canEdit && a.Status == AuditStatus.InProgress && a.Checklist.All(c => c.Result != ChecklistResult.NotAssessed)),
            ("close", canClose && a.Status == AuditStatus.Reporting && findings.All(f => f.Status is FindingStatus.Verified or FindingStatus.Closed)));
        return new
        {
            a.Id, a.Number, a.Title, a.AuditType, a.Scope, a.Criteria, a.SiteId, a.DepartmentId, a.LeadAuditorId, LeadName = names[a.LeadAuditorId], a.PlannedStart, a.PlannedEnd,
            a.Status, a.Summary, a.ClosedAt, Checklist = a.Checklist.OrderBy(c => c.Sequence),
            Findings = findings.Select(f => new { f.Id, f.Number, f.FindingType, f.Clause, f.Statement, f.Evidence, f.Status, f.ResponseDueOn, f.ChecklistItemId, ResponsibleName = names[f.ResponsibleId], f.VerificationNotes }),
            SiteCode = await db.Sites.Where(s => s.Id == a.SiteId).Select(s => s.Code).FirstAsync(ct),
            CanEdit = canEdit, CanClose = canClose, Allowed = allowed
        };
    }

    [HttpGet("findings/{id:guid}")]
    public async Task<object> Finding(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(AuditService.FindingType, id, ct);
        var f = await db.AuditFindings.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.AuditsEdit, f.SiteId, f.DepartmentId, ct);
        var isNc = f.FindingType is FindingType.MajorNonconformity or FindingType.MinorNonconformity;
        var open = f.Status is FindingStatus.Open or FindingStatus.ActionPlanned;
        var allowed = Allowed.Of(("verify", canEdit && open && await Allowed.ActionsClosedAsync(db, AuditService.FindingType, id, isNc, ct)), ("actions", canEdit && open));
        return new
        {
            f.Id, f.Number, f.AuditId, AuditNumber = await db.Audits.Where(a => a.Id == f.AuditId).Select(a => a.Number).FirstAsync(ct), f.FindingType, f.Clause, f.Statement, f.Evidence,
            f.Status, f.ResponseDueOn, f.VerificationNotes, ResponsibleName = await db.Users.Where(u => u.Id == f.ResponsibleId).Select(u => u.DisplayName).FirstAsync(ct),
            CanEdit = canEdit, Allowed = allowed
        };
    }

    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct) { await audits.StartAsync(id, ct); return NoContent(); }

    [HttpPut("{id:guid}/checklist")]
    public async Task<IActionResult> Checklist(Guid id, List<ChecklistResponse> r, CancellationToken ct) { await audits.RecordResponsesAsync(id, r, ct); return NoContent(); }

    [HttpPost("{id:guid}/findings")]
    public async Task<object> AddFinding(Guid id, CreateFindingRequest r, CancellationToken ct) { var f = await audits.AddFindingAsync(id, r, ct); return new { f.Id, f.Number }; }

    [HttpPost("{id:guid}/reporting")]
    public async Task<IActionResult> Reporting(Guid id, TextRequest r, CancellationToken ct) { await audits.MoveToReportingAsync(id, r.Text, ct); return NoContent(); }

    /// <summary>Raises a CAPA from an audit finding (linked both ways); the finding can be verified once that CAPA is closed.</summary>
    [HttpPost("findings/{id:guid}/capa")]
    public async Task<object> FindingCapa(Guid id, NcrsController.CreateCapaFromNcr r, [FromServices] CapaService capas, CancellationToken ct)
    {
        await m.EditAsync(AuditService.FindingType, id, ct);
        var f = await db.AuditFindings.FirstAsync(x => x.Id == id, ct);
        if (f.Status is FindingStatus.Verified or FindingStatus.Closed) throw new ConflictException("The finding is already verified.");
        var c = await capas.CreateAsync(new CreateCapaRequest(string.IsNullOrWhiteSpace(r.Title) ? $"Audit finding {f.Number}" : r.Title, string.IsNullOrWhiteSpace(r.ProblemStatement) ? f.Statement : r.ProblemStatement,
            "Corrective", "Audit", f.SiteId, f.DepartmentId, r.OwnerId), ct, (AuditService.FindingType, f.Id));
        if (f.Status == FindingStatus.Open) { f.Status = FindingStatus.ActionPlanned; await db.SaveChangesAsync(ct); }
        return new { c.Id, c.Number };
    }

    [HttpPost("findings/{id:guid}/verify")]
    public async Task<IActionResult> VerifyFinding(Guid id, TextRequest r, CancellationToken ct) { await audits.VerifyFindingAsync(id, r.Text, ct); return NoContent(); }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct) { await audits.CloseAsync(id, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/training")]
public sealed class TrainingController(AppDbContext db, TrainingService training, ModuleSupport m) : ControllerBase
{
    public sealed record CompleteRequest(string Password, string? Notes);

    [HttpGet("courses")]
    [RequirePermission(Permissions.TrainingView)]
    public async Task<object> Courses([FromQuery] string? q, CancellationToken ct)
    {
        var query = await m.ScopeAsync(db.TrainingCourses.AsNoTracking().Where(c => !c.IsArchived), Permissions.TrainingView, ct);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title, $"%{q.Trim()}%"));
        return await query.OrderBy(x => x.Number).Select(x => new
        {
            x.Id, x.Number, x.Title, x.DueDays, x.ValidityMonths, x.RetrainOnRevision,
            DocumentNumber = db.Documents.Where(d => d.Id == x.DocumentId).Select(d => d.Number).FirstOrDefault(),
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First(),
            Open = db.TrainingAssignments.Count(a => a.CourseId == x.Id && a.Status == AssignmentStatus.Assigned),
            Completed = db.TrainingAssignments.Count(a => a.CourseId == x.Id && a.Status == AssignmentStatus.Completed)
        }).ToListAsync(ct);
    }

    [HttpPost("courses")]
    [RequirePermission(Permissions.TrainingEdit)]
    public async Task<object> Create(CreateCourseRequest req, CancellationToken ct) => new { (await training.CreateCourseAsync(req, ct)).Id };

    [HttpGet("courses/{id:guid}")]
    public async Task<object> Course(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(TrainingService.RecordType, id, ct);
        var c = await db.TrainingCourses.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var assignments = await db.TrainingAssignments.AsNoTracking().Where(a => a.CourseId == id).OrderByDescending(a => a.CreatedAt).Select(a => new
        {
            a.Id, a.UserId, UserName = db.Users.Where(u => u.Id == a.UserId).Select(u => u.DisplayName).First(), a.DocumentRevision, a.DueOn, a.Status, a.Reason, a.CompletedAt, a.ExpiresOn
        }).ToListAsync(ct);
        return new
        {
            c.Id, c.Number, c.Title, c.Description, c.SiteId, c.DepartmentId, c.DocumentId, c.RetrainOnRevision, c.ValidityMonths, c.DueDays,
            DocumentNumber = await db.Documents.Where(d => d.Id == c.DocumentId).Select(d => d.Number + " " + d.Title).FirstOrDefaultAsync(ct),
            Assignments = assignments, CanEdit = await m.CanAsync(Permissions.TrainingEdit, c.SiteId, c.DepartmentId, ct)
        };
    }

    [HttpPost("courses/{id:guid}/assign")]
    public async Task<object> Assign(Guid id, AssignRequest r, CancellationToken ct) => new { Created = await training.AssignAsync(id, r, ct) };

    /// <summary>The signed-in user's own training; available to everyone.</summary>
    [HttpGet("my")]
    public async Task<object> My(CancellationToken ct)
    {
        var uid = m.UserId;
        var today = await m.TodayAsync(null, ct);
        return await db.TrainingAssignments.AsNoTracking().Where(a => a.UserId == uid && (a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.Completed))
            .OrderBy(a => a.Status).ThenBy(a => a.DueOn).Select(a => new
            {
                a.Id, a.CourseId, CourseNumber = db.TrainingCourses.Where(c => c.Id == a.CourseId).Select(c => c.Number).First(),
                CourseTitle = db.TrainingCourses.Where(c => c.Id == a.CourseId).Select(c => c.Title).First(),
                DocumentId = db.TrainingCourses.Where(c => c.Id == a.CourseId).Select(c => c.DocumentId).First(),
                a.DocumentRevision, a.DueOn, a.Status, a.Reason, a.CompletedAt, a.ExpiresOn, Overdue = a.Status == AssignmentStatus.Assigned && a.DueOn < today
            }).ToListAsync(ct);
    }

    [HttpGet("matrix")]
    [RequirePermission(Permissions.TrainingView)]
    public async Task<object> Matrix(CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.TrainingAssignments.AsNoTracking().Where(a => a.Status == AssignmentStatus.Assigned || a.Status == AssignmentStatus.Completed), Permissions.TrainingView, ct);
        var today = await m.TodayAsync(null, ct);
        return await q.OrderBy(a => a.DueOn).Select(a => new
        {
            a.Id, UserName = db.Users.Where(u => u.Id == a.UserId).Select(u => u.DisplayName).First(),
            CourseNumber = db.TrainingCourses.Where(c => c.Id == a.CourseId).Select(c => c.Number).First(), a.CourseId,
            CourseTitle = db.TrainingCourses.Where(c => c.Id == a.CourseId).Select(c => c.Title).First(),
            a.DocumentRevision, a.DueOn, a.Status, a.ExpiresOn, Overdue = a.Status == AssignmentStatus.Assigned && a.DueOn < today
        }).Take(1000).ToListAsync(ct);
    }

    [HttpPost("assignments/{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CompleteRequest r, CancellationToken ct) { await training.CompleteAsync(id, r.Password, r.Notes, ct); return NoContent(); }
}

[ApiController, Authorize, Route("api/calibration")]
public sealed class CalibrationController(AppDbContext db, CalibrationService cal, MaintenanceService mnt, ModuleSupport m) : ControllerBase
{
    public sealed record RaiseNcrRequest(string Title, string Description);

    [HttpGet("equipment")]
    [RequirePermission(Permissions.CalibrationView)]
    public async Task<object> Equipment([FromQuery] string? q, [FromQuery] EquipmentStatus? status, [FromQuery] int? dueWithinDays, [FromQuery] string? category, [FromQuery] bool? calibrated, [FromQuery] int? maintenanceDueWithinDays, CancellationToken ct)
    {
        var query = await m.ScopeAsync(db.Equipment.AsNoTracking(), Permissions.CalibrationView, ct);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);
        if (calibrated is { } c) query = query.Where(x => x.RequiresCalibration == c);
        if (maintenanceDueWithinDays is { } md) { var lim = (await m.TodayAsync(null, ct)).AddDays(md); query = query.Where(x => x.Status != EquipmentStatus.Retired && db.MaintenancePlans.Any(p => p.EquipmentId == x.Id && p.IsActive && p.NextDueOn <= lim)); }
        if (status is not null) query = query.Where(x => x.Status == status);
        if (dueWithinDays is { } d) { var lim = (await m.TodayAsync(null, ct)).AddDays(d); query = query.Where(x => x.Status != EquipmentStatus.Retired && x.NextDueOn != null && x.NextDueOn <= lim); }
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Name + " " + (x.SerialNumber ?? ""), $"%{q.Trim()}%"));
        var today = await m.TodayAsync(null, ct);
        return await query.OrderBy(x => x.NextDueOn).Select(x => new
        {
            x.Id, x.Number, x.Name, x.SerialNumber, x.Location, x.Status, x.LastCalibratedOn, x.NextDueOn, x.CalibrationIntervalDays, x.Category, x.AssetNumber, x.RequiresCalibration,
            NextMaintenanceOn = db.MaintenancePlans.Where(p => p.EquipmentId == x.Id && p.IsActive).Min(p => (DateOnly?)p.NextDueOn),
            Overdue = x.NextDueOn != null && x.NextDueOn < today && x.Status != EquipmentStatus.Retired,
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First()
        }).Take(1000).ToListAsync(ct);
    }

    [HttpPost("equipment")]
    [RequirePermission(Permissions.CalibrationEdit)]
    public async Task<object> Create(CreateEquipmentRequest req, CancellationToken ct) => new { (await cal.CreateAsync(req, ct)).Id };

    [HttpGet("equipment/{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(CalibrationService.EquipmentType, id, ct);
        var e = await db.Equipment.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.CalibrationEdit, e.SiteId, e.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.CalibrationApprove, e.SiteId, e.DepartmentId, ct);
        var latest = await db.CalibrationEvents.AsNoTracking().Where(x => x.EquipmentId == id).OrderByDescending(x => x.PerformedOn).ThenByDescending(x => x.CreatedAt).Select(x => (CalibrationResult?)x.Result).FirstOrDefaultAsync(ct);
        var openIa = await db.ImpactAssessments.AnyAsync(x => x.EquipmentId == id && x.Status == ImpactStatus.Open, ct);
        var canMaintain = await m.CanAsync(Permissions.MaintenanceEdit, e.SiteId, e.DepartmentId, ct);
        var latestOn = await db.CalibrationEvents.AsNoTracking().Where(x => x.EquipmentId == id).MaxAsync(x => (DateOnly?)x.PerformedOn, ct);
        var live = e.Status != EquipmentStatus.Retired;
        var allowed = Allowed.Of(("record", canEdit && live && e.RequiresCalibration), ("edit", canEdit && live), ("retire", canApprove && live && !openIa),
            ("returnToService", canApprove && e.Status is EquipmentStatus.Quarantined or EquipmentStatus.OutOfService && !openIa && latest is CalibrationResult.Pass or CalibrationResult.PassAfterAdjustment
                && (e.RecalibrateAfter == null || latestOn >= e.RecalibrateAfter)),
            ("maintain", canMaintain && live), ("repair", canMaintain && e.Status == EquipmentStatus.Down));
        return new
        {
            e.Id, e.Number, e.Name, e.Manufacturer, e.Model, e.SerialNumber, e.Location, e.MeasurementRange, e.Tolerance, e.SiteId, e.DepartmentId, e.Status,
            e.CalibrationIntervalDays, e.LastCalibratedOn, e.NextDueOn, e.OwnerId, e.Category, e.AssetNumber, e.RequiresCalibration, e.RecalibrateAfter, e.QualifiedOn, e.QualificationDueOn, e.QualificationNotes, e.RowVersion,
            OwnerName = await db.Users.Where(u => u.Id == e.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(s => s.Id == e.SiteId).Select(s => s.Code).FirstAsync(ct),
            Plans = await db.MaintenancePlans.AsNoTracking().Where(p => p.EquipmentId == id && p.IsActive).OrderBy(p => p.NextDueOn).ToListAsync(ct),
            Maintenance = await db.MaintenanceEvents.AsNoTracking().Where(x => x.EquipmentId == id).OrderByDescending(x => x.PerformedOn).ThenByDescending(x => x.CreatedAt)
                .Select(x => new { x.Id, x.Number, x.Kind, x.PerformedOn, x.PerformedBy, x.Description, x.DowntimeHours, x.ReturnedToService, x.CalibrationRequired, PlanTitle = db.MaintenancePlans.Where(p => p.Id == x.PlanId).Select(p => p.Title).FirstOrDefault() }).ToListAsync(ct),
            Inspections = await db.Inspections.AsNoTracking().Where(i => db.InspectionResults.Any(r => r.InspectionId == i.Id && r.EquipmentId == id)).OrderByDescending(i => i.CreatedAt).Take(50)
                .Select(i => new { i.Id, i.Number, PlanNumber = db.InspectionPlans.Where(p => p.Id == i.PlanId).Select(p => p.Number).First(), i.PlanRevision, i.Status, i.CreatedAt }).ToListAsync(ct),
            Downtime = await db.MaintenanceEvents.Where(x => x.EquipmentId == id).SumAsync(x => x.DowntimeHours ?? 0, ct),
            Events = await db.CalibrationEvents.AsNoTracking().Where(x => x.EquipmentId == id).OrderByDescending(x => x.PerformedOn).ThenByDescending(x => x.CreatedAt).ToListAsync(ct),
            Impacts = await db.ImpactAssessments.AsNoTracking().Where(x => x.EquipmentId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct),
            CanEdit = canEdit, CanApprove = canApprove, Allowed = allowed
        };
    }

    [HttpPost("equipment/{id:guid}/events")]
    public async Task<object> Record(Guid id, RecordCalibrationRequest r, CancellationToken ct)
    {
        var (ev, ia) = await cal.RecordAsync(id, r, ct);
        return new { EventId = ev.Id, ev.Number, ImpactAssessmentId = ia?.Id, ImpactNumber = ia?.Number };
    }

    [HttpGet("equipment-categories")] public string[] Categories() => CalibrationService.Categories;
    [HttpPut("equipment/{id:guid}")] public async Task<IActionResult> Update(Guid id, UpdateEquipmentRequest r, CancellationToken ct) { await cal.UpdateAsync(id, r, ct); return NoContent(); }
    [HttpPost("equipment/{id:guid}/retire")] public async Task<IActionResult> Retire(Guid id, CancellationToken ct) { await cal.RetireAsync(id, ct); return NoContent(); }
    [HttpPost("equipment/{id:guid}/maintenance-plans")] public async Task<object> AddPlan(Guid id, MaintenancePlanRequest r, CancellationToken ct) => new { (await mnt.AddPlanAsync(id, r, ct)).Id };
    [HttpDelete("equipment/{id:guid}/maintenance-plans/{planId:guid}")] public async Task<IActionResult> DeactivatePlan(Guid id, Guid planId, CancellationToken ct) { await mnt.DeactivatePlanAsync(id, planId, ct); return NoContent(); }
    [HttpPost("equipment/{id:guid}/maintenance")] public async Task<object> Maintain(Guid id, RecordMaintenanceRequest r, CancellationToken ct) { var e = await mnt.RecordAsync(id, r, ct); return new { e.Id, e.Number }; }

    [HttpPost("equipment/{id:guid}/return-to-service")]
    public async Task<IActionResult> ReturnToService(Guid id, CancellationToken ct) { await cal.ReturnToServiceAsync(id, ct); return NoContent(); }

    [HttpGet("impact/{id:guid}")]
    public async Task<object> Impact(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(CalibrationService.ImpactType, id, ct);
        var ia = await db.ImpactAssessments.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var e = await db.Equipment.AsNoTracking().FirstAsync(x => x.Id == ia.EquipmentId, ct);
        var ev = await db.CalibrationEvents.AsNoTracking().FirstAsync(x => x.Id == ia.CalibrationEventId, ct);
        var ncrIds = await db.RecordLinks.Where(l => l.FromType == CalibrationService.ImpactType && l.FromId == id && l.ToType == NcrService.RecordType).Select(l => l.ToId).ToListAsync(ct);
        return new
        {
            ia.Id, ia.Number, ia.Status, ia.LastKnownGoodOn, ia.AffectedUntil, ia.AffectedProduct, ia.Assessment, ia.Decision, ia.CompletedAt,
            Equipment = new { e.Id, e.Number, e.Name }, Event = new { ev.Id, ev.Number, ev.PerformedOn, ev.Result, ev.AsFound },
            Ncrs = await db.Ncrs.Where(n => ncrIds.Contains(n.Id)).Select(n => new { n.Id, n.Number, n.Status }).ToListAsync(ct),
            CanApprove = await m.CanAsync(Permissions.CalibrationApprove, ia.SiteId, ia.DepartmentId, ct),
            Allowed = Allowed.Of(("complete", ia.Status == ImpactStatus.Open && await m.CanAsync(Permissions.CalibrationApprove, ia.SiteId, ia.DepartmentId, ct)),
                ("ncr", await m.CanAsync(Permissions.NcrEdit, ia.SiteId, ia.DepartmentId, ct)))
        };
    }

    [HttpPost("impact/{id:guid}/complete")]
    public async Task<IActionResult> CompleteImpact(Guid id, CompleteImpactRequest r, CancellationToken ct) { await cal.CompleteImpactAsync(id, r, ct); return NoContent(); }

    [HttpPost("impact/{id:guid}/ncr")]
    public async Task<object> RaiseNcr(Guid id, RaiseNcrRequest r, CancellationToken ct) { var n = await cal.RaiseNcrAsync(id, r.Title, r.Description, ct); return new { n.Id, n.Number }; }
}
