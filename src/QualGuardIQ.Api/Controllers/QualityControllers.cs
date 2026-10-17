using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
        var (r, fields, values) = await forms.GetAsync(id, ct);
        var editable = r.Status is FormRecordStatus.Draft or FormRecordStatus.Rejected && await m.CanAsync(Permissions.FormsEdit, r.SiteId, r.DepartmentId, ct);
        return new { r.Id, r.Number, r.Title, r.TemplateId, r.TemplateNumber, r.TemplateRevision, r.Status, r.SiteId, r.SubmittedAt, r.RowVersion, Fields = fields, Values = values, CanEdit = editable };
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
    public async Task<object> List([FromQuery] string? q, [FromQuery] NcrStatus? status, [FromQuery] bool openOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (openOnly) query = query.Where(x => x.Status != NcrStatus.Closed && x.Status != NcrStatus.Cancelled);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + (x.PartNumber ?? "") + " " + (x.LotNumber ?? ""), $"%{q.Trim()}%"));
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
        return new
        {
            n.Id, n.Number, n.Title, n.Description, n.Source, n.SiteId, n.DepartmentId, n.OwnerId, n.PartNumber, n.LotNumber, n.QuantityAffected, n.DetectedOn, n.Severity,
            n.Status, n.ContainmentSummary, n.Disposition, n.DispositionJustification, n.CapaRequired, n.ClosureNotes, n.ClosedAt, n.CreatedAt,
            OwnerName = await db.Users.Where(u => u.Id == n.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(s => s.Id == n.SiteId).Select(s => s.Code).FirstAsync(ct),
            Capas = linkedCapas, CanEdit = await m.CanAsync(Permissions.NcrEdit, n.SiteId, n.DepartmentId, ct)
        };
    }

    [HttpPost("{id:guid}/containment")]
    public async Task<IActionResult> Contain(Guid id, TextRequest r, CancellationToken ct) { await ncrs.ContainAsync(id, r.Text, ct); return NoContent(); }

    [HttpPost("{id:guid}/disposition")]
    public async Task<IActionResult> Disposition(Guid id, DispositionRequest r, CancellationToken ct) { await ncrs.ProposeDispositionAsync(id, r, ct); return NoContent(); }

    [HttpPost("{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, TextRequest r, CancellationToken ct) { await ncrs.CloseAsync(id, r.Text, ct); return NoContent(); }

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
        return new
        {
            c.Id, c.Number, c.Title, c.ProblemStatement, c.CapaType, c.Source, c.SiteId, c.DepartmentId, c.OwnerId, c.Status, c.RootCauseMethod, c.RootCause,
            c.EffectivenessCriteria, c.EffectivenessDueOn, c.EffectivenessResult, c.WasEffective, c.ClosedAt, c.CreatedAt,
            OwnerName = await db.Users.Where(u => u.Id == c.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            VerifiedByName = c.EffectivenessVerifiedById == null ? null : await db.Users.Where(u => u.Id == c.EffectivenessVerifiedById).Select(u => u.DisplayName).FirstAsync(ct),
            SiteCode = await db.Sites.Where(s => s.Id == c.SiteId).Select(s => s.Code).FirstAsync(ct),
            CanEdit = await m.CanAsync(Permissions.CapaEdit, c.SiteId, c.DepartmentId, ct),
            CanVerify = c.OwnerId != m.UserId && await m.CanAsync(Permissions.CapaApprove, c.SiteId, c.DepartmentId, ct)
        };
    }

    [HttpPost("{id:guid}/root-cause")]
    public async Task<IActionResult> RootCause(Guid id, RootCauseRequest r, CancellationToken ct) { await capas.RecordRootCauseAsync(id, r, ct); return NoContent(); }

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
        return new
        {
            a.Id, a.Number, a.Title, a.AuditType, a.Scope, a.Criteria, a.SiteId, a.DepartmentId, a.LeadAuditorId, LeadName = names[a.LeadAuditorId], a.PlannedStart, a.PlannedEnd,
            a.Status, a.Summary, a.ClosedAt, Checklist = a.Checklist.OrderBy(c => c.Sequence),
            Findings = findings.Select(f => new { f.Id, f.Number, f.FindingType, f.Clause, f.Statement, f.Evidence, f.Status, f.ResponseDueOn, f.ChecklistItemId, ResponsibleName = names[f.ResponsibleId], f.VerificationNotes }),
            SiteCode = await db.Sites.Where(s => s.Id == a.SiteId).Select(s => s.Code).FirstAsync(ct),
            CanEdit = await m.CanAsync(Permissions.AuditsEdit, a.SiteId, a.DepartmentId, ct),
            CanClose = await m.CanAsync(Permissions.AuditsApprove, a.SiteId, a.DepartmentId, ct)
        };
    }

    [HttpGet("findings/{id:guid}")]
    public async Task<object> Finding(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(AuditService.FindingType, id, ct);
        var f = await db.AuditFindings.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return new
        {
            f.Id, f.Number, f.AuditId, AuditNumber = await db.Audits.Where(a => a.Id == f.AuditId).Select(a => a.Number).FirstAsync(ct), f.FindingType, f.Clause, f.Statement, f.Evidence,
            f.Status, f.ResponseDueOn, f.VerificationNotes, ResponsibleName = await db.Users.Where(u => u.Id == f.ResponsibleId).Select(u => u.DisplayName).FirstAsync(ct),
            CanEdit = await m.CanAsync(Permissions.AuditsEdit, f.SiteId, f.DepartmentId, ct)
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
        var today = m.Today;
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
        var today = m.Today;
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
public sealed class CalibrationController(AppDbContext db, CalibrationService cal, ModuleSupport m) : ControllerBase
{
    public sealed record RaiseNcrRequest(string Title, string Description);

    [HttpGet("equipment")]
    [RequirePermission(Permissions.CalibrationView)]
    public async Task<object> Equipment([FromQuery] string? q, [FromQuery] EquipmentStatus? status, [FromQuery] int? dueWithinDays, CancellationToken ct)
    {
        var query = await m.ScopeAsync(db.Equipment.AsNoTracking(), Permissions.CalibrationView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (dueWithinDays is { } d) { var lim = m.Today.AddDays(d); query = query.Where(x => x.Status != EquipmentStatus.Retired && x.NextDueOn != null && x.NextDueOn <= lim); }
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Name + " " + (x.SerialNumber ?? ""), $"%{q.Trim()}%"));
        var today = m.Today;
        return await query.OrderBy(x => x.NextDueOn).Select(x => new
        {
            x.Id, x.Number, x.Name, x.SerialNumber, x.Location, x.Status, x.LastCalibratedOn, x.NextDueOn, x.CalibrationIntervalDays,
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
        return new
        {
            e.Id, e.Number, e.Name, e.Manufacturer, e.Model, e.SerialNumber, e.Location, e.MeasurementRange, e.Tolerance, e.SiteId, e.DepartmentId, e.Status,
            e.CalibrationIntervalDays, e.LastCalibratedOn, e.NextDueOn, OwnerName = await db.Users.Where(u => u.Id == e.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            Events = await db.CalibrationEvents.AsNoTracking().Where(x => x.EquipmentId == id).OrderByDescending(x => x.PerformedOn).ThenByDescending(x => x.CreatedAt).ToListAsync(ct),
            Impacts = await db.ImpactAssessments.AsNoTracking().Where(x => x.EquipmentId == id).OrderByDescending(x => x.CreatedAt).ToListAsync(ct),
            CanEdit = await m.CanAsync(Permissions.CalibrationEdit, e.SiteId, e.DepartmentId, ct),
            CanApprove = await m.CanAsync(Permissions.CalibrationApprove, e.SiteId, e.DepartmentId, ct)
        };
    }

    [HttpPost("equipment/{id:guid}/events")]
    public async Task<object> Record(Guid id, RecordCalibrationRequest r, CancellationToken ct)
    {
        var (ev, ia) = await cal.RecordAsync(id, r, ct);
        return new { EventId = ev.Id, ev.Number, ImpactAssessmentId = ia?.Id, ImpactNumber = ia?.Number };
    }

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
            CanApprove = await m.CanAsync(Permissions.CalibrationApprove, ia.SiteId, ia.DepartmentId, ct)
        };
    }

    [HttpPost("impact/{id:guid}/complete")]
    public async Task<IActionResult> CompleteImpact(Guid id, CompleteImpactRequest r, CancellationToken ct) { await cal.CompleteImpactAsync(id, r, ct); return NoContent(); }

    [HttpPost("impact/{id:guid}/ncr")]
    public async Task<object> RaiseNcr(Guid id, RaiseNcrRequest r, CancellationToken ct) { var n = await cal.RaiseNcrAsync(id, r.Title, r.Description, ct); return new { n.Id, n.Number }; }
}
