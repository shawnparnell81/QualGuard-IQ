using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.QualityOps;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

public sealed record StatusRequest(string Status, string? Comment);
public sealed record UseRequest(decimal Quantity, string? Lot);
public sealed record LotRecordRequest(Guid SiteId, Guid PartId, Guid? SupplierId, string? Lot, bool Inspected, bool? Accepted);

/// <summary>Quality operations: events, deviations, investigations, release, delegation, registers, service levels, skip-lot, quality reviews, analytics.</summary>
[ApiController, Authorize, Route("api/quality-ops")]
public sealed class QualityOpsController(AppDbContext db, ModuleSupport m, QualityEventService events, DeviationService deviations, InvestigationService investigations,
    ReleaseService releases, DelegationService delegations, RegisterService registers, ServiceLevelService serviceLevels, SkipLotService skipLots,
    QualityReviewService reviews, QualityAnalyticsService analytics) : ControllerBase
{
    static string? Q(string? q) => string.IsNullOrWhiteSpace(q) ? null : $"%{q.Trim()}%";
    string Name(Guid? id) => id is null ? "" : db.Users.Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefault() ?? "";

    // ---------- settings (severity matrix) ----------
    [HttpGet("settings")] [RequirePermission(Permissions.QualityOpsView)] public Task<QualityOpsSettingsDto> Settings(CancellationToken ct) => events.GetSettingsAsync(ct);
    [HttpGet("settings/intake")] public async Task<object> IntakeSettings(CancellationToken ct) { var s = await events.GetSettingsAsync(ct); return new { s.Categories }; }
    [HttpPut("settings")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> SaveSettings(QualityOpsSettingsDto r, CancellationToken ct) { await events.SaveSettingsAsync(r, ct); return NoContent(); }

    // ---------- quality events ----------
    [HttpGet("events")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Events([FromQuery] string? q, [FromQuery] QualityEventStatus? status, [FromQuery] string? severity, [FromQuery] QualityEventSource? source,
        [FromQuery] bool openOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.QualityEvents.AsNoTracking(), Permissions.QualityOpsView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (source is not null) query = query.Where(x => x.Source == source);
        if (!string.IsNullOrWhiteSpace(severity)) query = query.Where(x => x.Severity == severity);
        if (openOnly) query = query.Where(x => x.Status != QualityEventStatus.Closed && x.Status != QualityEventStatus.Rejected);
        if (Q(q) is { } like) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + (x.Category ?? "") + " " + (x.LotNumber ?? ""), like));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Title, x.Source, x.Category, x.Classification, x.Severity, x.Status, x.OccurredAt, x.RespondBy, x.LotNumber, x.EscalatedToNumber,
            PartNumber = db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).FirstOrDefault(),
            ReportedBy = db.Users.Where(u => u.Id == x.ReportedById).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    /// <summary>Reports a quality event. Requires qualityevents.report or qualityops.edit at the site; API clients use the source and client reference.</summary>
    [HttpPost("events")] public Task<IntakeResult> Report(QualityEventInput r, CancellationToken ct) => events.ReportAsync(r, ct);

    [HttpGet("events/{id:guid}")]
    public async Task<object> Event(Guid id, CancellationToken ct)
    {
        var info = await m.ViewAsync(QualityEventService.RecordType, id, ct);
        var x = await db.QualityEvents.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.QualityOpsEdit, x.SiteId, x.DepartmentId, ct);
        return new
        {
            Event = x, ReportedBy = Name(x.ReportedById), TriagedBy = Name(x.TriagedById),
            PartNumber = await db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).FirstOrDefaultAsync(ct),
            Supplier = await db.Suppliers.Where(p => p.Id == x.SupplierId).Select(p => p.LegalName).FirstOrDefaultAsync(ct),
            Customer = await db.Customers.Where(p => p.Id == x.CustomerId).Select(p => p.Name).FirstOrDefaultAsync(ct),
            Settings = await events.GetSettingsAsync(ct),
            Actions = new
            {
                Triage = canEdit && x.Status == QualityEventStatus.New,
                Classify = canEdit && x.Status is QualityEventStatus.New or QualityEventStatus.InTriage or QualityEventStatus.Classified,
                Escalate = canEdit && x.Status == QualityEventStatus.Classified,
                Close = canEdit && x.Status is QualityEventStatus.InTriage or QualityEventStatus.Classified or QualityEventStatus.Escalated,
                Reject = canEdit && x.Status is QualityEventStatus.New or QualityEventStatus.InTriage
            },
            info.Url
        };
    }
    [HttpPost("events/{id:guid}/triage")] public async Task<IActionResult> Triage(Guid id, CancellationToken ct) { await events.StartTriageAsync(id, ct); return NoContent(); }
    [HttpPost("events/{id:guid}/classify")] public async Task<IActionResult> Classify(Guid id, TriageInput r, CancellationToken ct) { await events.ClassifyAsync(id, r, ct); return NoContent(); }
    [HttpPost("events/{id:guid}/escalate")]
    public async Task<object> Escalate(Guid id, EscalateInput r, CancellationToken ct) { var t = await events.EscalateAsync(id, r, deviations, investigations, ct); return new { t.Type, t.Id, t.Number }; }
    [HttpPost("events/{id:guid}/close")] public async Task<IActionResult> CloseEvent(Guid id, NotesRequest r, CancellationToken ct) { await events.CloseAsync(id, r.Notes ?? "", ct); return NoContent(); }
    [HttpPost("events/{id:guid}/reject")] public async Task<IActionResult> RejectEvent(Guid id, ReasonRequest r, CancellationToken ct) { await events.RejectAsync(id, r.Reason, ct); return NoContent(); }

    // ---------- deviations ----------
    [HttpGet("deviations")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Deviations([FromQuery] string? q, [FromQuery] QualityDeviationStatus? status, [FromQuery] DeviationKind? kind, [FromQuery] bool inForce = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Deviations.AsNoTracking(), Permissions.QualityOpsView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (kind is not null) query = query.Where(x => x.Kind == kind);
        if (inForce) { var today = await m.TodayAsync(null, ct); query = query.Where(x => x.Status == QualityDeviationStatus.Approved && (x.EffectiveFrom == null || x.EffectiveFrom <= today) && (x.EffectiveTo == null || x.EffectiveTo >= today)); }
        if (Q(q) is { } like) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + (x.AffectedLots ?? ""), like));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Kind, x.Title, x.Status, x.EffectiveFrom, x.EffectiveTo, x.QuantityLimit, x.QuantityUsed,
            PartNumber = db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).FirstOrDefault(), Owner = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }
    [HttpPost("deviations")] [RequirePermission(Permissions.QualityOpsEdit)]
    public async Task<object> CreateDeviation(DeviationInput r, CancellationToken ct) { var x = await deviations.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpGet("deviations/{id:guid}")]
    public async Task<object> Deviation(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(DeviationService.RecordType, id, ct);
        var x = await db.Deviations.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.QualityOpsEdit, x.SiteId, x.DepartmentId, ct);
        var today = await m.TodayAsync(x.SiteId, ct);
        var editable = x.Status is QualityDeviationStatus.Draft or QualityDeviationStatus.Rejected;
        return new
        {
            Deviation = x, Owner = Name(x.OwnerId), InForce = DeviationService.InForce(x, today),
            PartNumber = await db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).FirstOrDefaultAsync(ct),
            Actions = new { Edit = canEdit && editable, Submit = canEdit && editable, Use = canEdit && DeviationService.InForce(x, today), Close = canEdit && x.Status == QualityDeviationStatus.Approved, Cancel = canEdit && editable }
        };
    }
    [HttpPut("deviations/{id:guid}")] public async Task<IActionResult> UpdateDeviation(Guid id, DeviationInput r, CancellationToken ct) { await deviations.UpdateAsync(id, r, ct); return NoContent(); }
    [HttpPost("deviations/{id:guid}/submit")] public async Task<IActionResult> SubmitDeviation(Guid id, CancellationToken ct) { await deviations.SubmitAsync(id, ct); return NoContent(); }
    [HttpPost("deviations/{id:guid}/use")] public async Task<IActionResult> UseDeviation(Guid id, UseRequest r, CancellationToken ct) { await deviations.RecordUseAsync(id, r.Quantity, r.Lot, ct); return NoContent(); }
    [HttpPost("deviations/{id:guid}/close")] public async Task<IActionResult> CloseDeviation(Guid id, NotesRequest r, CancellationToken ct) { await deviations.CloseAsync(id, r.Notes ?? "", ct); return NoContent(); }
    [HttpPost("deviations/{id:guid}/cancel")] public async Task<IActionResult> CancelDeviation(Guid id, ReasonRequest r, CancellationToken ct) { await deviations.CancelAsync(id, r.Reason, ct); return NoContent(); }

    // ---------- investigations ----------
    [HttpGet("investigations")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Investigations([FromQuery] string? q, [FromQuery] InvestigationStatus? status, [FromQuery] InvestigationKind? kind, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Investigations.AsNoTracking(), Permissions.QualityOpsView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (kind is not null) query = query.Where(x => x.Kind == kind);
        if (Q(q) is { } like) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + (x.SampleReference ?? ""), like));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Kind, x.Title, x.Status, x.EscalationLevel, x.DueOn, x.OutOfSpecification, Lead = db.Users.Where(u => u.Id == x.LeadId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }
    [HttpPost("investigations")] [RequirePermission(Permissions.QualityOpsEdit)]
    public async Task<object> CreateInvestigation(InvestigationInput r, CancellationToken ct) { var x = await investigations.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpGet("investigations/{id:guid}")]
    public async Task<object> Investigation(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(InvestigationService.RecordType, id, ct);
        var x = await db.Investigations.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.QualityOpsEdit, x.SiteId, x.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.QualityOpsApprove, x.SiteId, x.DepartmentId, ct);
        var open = x.Status is InvestigationStatus.Open or InvestigationStatus.InProgress;
        var links = await db.RecordLinks.AsNoTracking().Where(l => (l.FromType == InvestigationService.RecordType && l.FromId == id) || (l.ToType == InvestigationService.RecordType && l.ToId == id)).ToListAsync(ct);
        var linked = new List<object>();
        foreach (var l in links)
        {
            var (type, rid) = l.FromId == id ? (l.ToType, l.ToId) : (l.FromType, l.FromId);
            try { var info = await m.ViewAsync(type, rid, ct); linked.Add(new { info.Type, info.Id, info.Number, info.Title, info.Status, info.Url, l.LinkType }); }
            catch (AppException) { /* records the user may not view are left out */ }
        }
        return new
        {
            Investigation = x, Lead = Name(x.LeadId), Linked = linked,
            Actions = new { Work = canEdit && open, Link = canEdit && x.Status is not (InvestigationStatus.Closed or InvestigationStatus.Cancelled), Escalate = canEdit && open && x.EscalationLevel < 5,
                Conclude = canEdit && open, Close = canEdit && canApprove && x.Status == InvestigationStatus.Concluded, Cancel = canEdit && x.Status is not (InvestigationStatus.Closed or InvestigationStatus.Cancelled) }
        };
    }
    [HttpPut("investigations/{id:guid}/work")] public async Task<IActionResult> Work(Guid id, InvestigationWork r, CancellationToken ct) { await investigations.SaveWorkAsync(id, r, ct); return NoContent(); }
    [HttpPost("investigations/{id:guid}/links")] public async Task<IActionResult> Link(Guid id, LinkInput r, CancellationToken ct) { await investigations.LinkAsync(id, r, ct); return NoContent(); }
    [HttpPost("investigations/{id:guid}/escalate")] public async Task<object> EscalateInvestigation(Guid id, ReasonRequest r, CancellationToken ct) => new { Level = await investigations.EscalateAsync(id, r.Reason, ct) };
    [HttpPost("investigations/{id:guid}/conclude")] public async Task<IActionResult> Conclude(Guid id, NotesRequest r, CancellationToken ct) { await investigations.ConcludeAsync(id, r.Notes ?? "", ct); return NoContent(); }
    [HttpPost("investigations/{id:guid}/close")] public async Task<IActionResult> CloseInvestigation(Guid id, CancellationToken ct) { await investigations.CloseAsync(id, ct); return NoContent(); }
    [HttpPost("investigations/{id:guid}/cancel")] public async Task<IActionResult> CancelInvestigation(Guid id, ReasonRequest r, CancellationToken ct) { await investigations.CancelAsync(id, r.Reason, ct); return NoContent(); }

    // ---------- release ----------
    [HttpGet("releases")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Releases([FromQuery] string? q, [FromQuery] ReleaseStatus? status, [FromQuery] ReleaseKind? kind, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.ReleaseDecisions.AsNoTracking(), Permissions.QualityOpsView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (kind is not null) query = query.Where(x => x.Kind == kind);
        if (Q(q) is { } like) query = query.Where(x => EF.Functions.ILike(x.Number + " " + (x.LotNumber ?? ""), like));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Kind, x.Status, x.LotNumber, x.Quantity, x.DecidedByName, x.DecidedAt, PartNumber = db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).FirstOrDefault()
        }), page, pageSize, ct);
    }
    [HttpGet("releases/board")] [RequirePermission(Permissions.QualityOpsView)] public Task<object> Board(CancellationToken ct) => releases.BoardAsync(ct);
    [HttpPost("releases")] [RequirePermission(Permissions.QualityOpsEdit)]
    public async Task<object> CreateRelease(ReleaseInput r, CancellationToken ct) { var x = await releases.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpGet("releases/{id:guid}")]
    public async Task<object> Release(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ReleaseService.RecordType, id, ct);
        var x = await db.ReleaseDecisions.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        var checks = x.Status == ReleaseStatus.Pending ? await releases.ChecksAsync(x, ct) : System.Text.Json.JsonSerializer.Deserialize<List<ReleaseCheck>>(x.Checklist ?? "[]", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [];
        var canApprove = await m.CanAsync(Permissions.QualityOpsApprove, x.SiteId, x.DepartmentId, ct);
        var canEdit = await m.CanAsync(Permissions.QualityOpsEdit, x.SiteId, x.DepartmentId, ct);
        return new
        {
            Release = x, RequestedBy = Name(x.RequestedById), Checks = checks, SignatureValid = x.SignatureHash is null ? (bool?)null : x.SignatureHash == ReleaseService.Hash(x),
            PartNumber = await db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).FirstOrDefaultAsync(ct),
            Actions = new { Release = canApprove && x.Status == ReleaseStatus.Pending && checks.All(c => c.Passed), Reject = canApprove && x.Status == ReleaseStatus.Pending, Cancel = canEdit && x.Status == ReleaseStatus.Pending }
        };
    }
    [HttpPost("releases/{id:guid}/decision")] public async Task<object> Decide(Guid id, ReleaseDecisionInput r, CancellationToken ct) { var x = await releases.DecideAsync(id, r, ct); return new { x.Status, x.DecisionMeaning, x.DecidedAt }; }
    [HttpPost("releases/{id:guid}/cancel")] public async Task<IActionResult> CancelRelease(Guid id, ReasonRequest r, CancellationToken ct) { await releases.CancelAsync(id, r.Reason, ct); return NoContent(); }

    // ---------- delegations ----------
    [HttpGet("delegations")] public Task<object> Delegations([FromQuery] bool all = false, CancellationToken ct = default) => delegations.ListAsync(all, ct);
    [HttpPost("delegations")] public async Task<object> Delegate(DelegationInput r, CancellationToken ct) { var x = await delegations.CreateAsync(r, ct); return new { x.Id }; }
    [HttpPost("delegations/{id:guid}/revoke")] public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) { await delegations.RevokeAsync(id, ct); return NoContent(); }

    // ---------- registers ----------
    [HttpGet("registers")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Registers(CancellationToken ct)
    {
        var lab = (await events.GetSettingsAsync(ct)).LabSampleTracking;
        var scope = await m.ScopeAsync(db.RegisterEntries.AsNoTracking(), Permissions.QualityOpsView, ct);
        var counts = await scope.Where(x => !x.Archived).GroupBy(x => x.Kind).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return RegisterService.Definitions.Where(d => !d.NeedsSetting || lab).Select(d => new { d.Key, d.Name, d.Description, d.Fields, d.Statuses, d.Transitions, d.ClosedStatuses, d.UsesSupplier, d.UsesCustomer, d.UsesPart,
            Count = counts.FirstOrDefault(c => c.Key == d.Kind)?.Count ?? 0 });
    }
    [HttpGet("registers/{key}")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Register(string key, [FromQuery] string? q, [FromQuery] string? status, [FromQuery] bool archived = false, [FromQuery] bool expiring = false,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var def = RegisterService.Definition(key);
        var query = (await m.ScopeAsync(db.RegisterEntries.AsNoTracking(), Permissions.QualityOpsView, ct)).Where(x => x.Kind == def.Kind && x.Archived == archived);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
        if (expiring) { var soon = (await m.TodayAsync(null, ct)).AddDays(30); query = query.Where(x => (x.ExpiresOn != null && x.ExpiresOn <= soon) || (x.ReviewDueOn != null && x.ReviewDueOn <= soon)); }
        if (Q(q) is { } like) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + (x.Summary ?? ""), like));
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Title, x.Status, x.EffectiveOn, x.ReviewDueOn, x.ExpiresOn, Owner = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First(),
            Supplier = db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.LegalName).FirstOrDefault(), Customer = db.Customers.Where(s => s.Id == x.CustomerId).Select(s => s.Name).FirstOrDefault(),
            PartNumber = db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).FirstOrDefault()
        }), page, pageSize, ct);
    }
    [HttpPost("registers/{key}")] [RequirePermission(Permissions.QualityOpsEdit)]
    public async Task<object> CreateEntry(string key, RegisterInput r, CancellationToken ct) { var x = await registers.CreateAsync(key, r, ct); return new { x.Id, x.Number }; }
    [HttpGet("registers/entries/{id:guid}")]
    public async Task<object> Entry(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RegisterService.RecordType, id, ct);
        var x = await db.RegisterEntries.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        var def = RegisterService.Definition(x.Kind);
        var canEdit = await m.CanAsync(Permissions.QualityOpsEdit, x.SiteId, x.DepartmentId, ct);
        var closed = def.ClosedStatuses.Contains(x.Status);
        return new
        {
            Entry = x, Details = System.Text.Json.JsonDocument.Parse(x.Details).RootElement, Owner = Name(x.OwnerId), Register = new { def.Key, def.Name, def.Fields, def.UsesSupplier, def.UsesCustomer, def.UsesPart },
            Next = canEdit && !x.Archived && def.Transitions.TryGetValue(x.Status, out var next) ? next : [],
            Actions = new { Edit = canEdit && !x.Archived && !closed, Archive = canEdit && !x.Archived && closed }
        };
    }
    [HttpPut("registers/entries/{id:guid}")] public async Task<IActionResult> UpdateEntry(Guid id, RegisterInput r, CancellationToken ct) { await registers.UpdateAsync(id, r, ct); return NoContent(); }
    [HttpPost("registers/entries/{id:guid}/status")] public async Task<IActionResult> EntryStatus(Guid id, StatusRequest r, CancellationToken ct) { await registers.TransitionAsync(id, r.Status, r.Comment, ct); return NoContent(); }
    [HttpPost("registers/entries/{id:guid}/archive")] public async Task<IActionResult> ArchiveEntry(Guid id, CancellationToken ct) { await registers.ArchiveAsync(id, ct); return NoContent(); }

    // ---------- calendars and service levels ----------
    [HttpGet("calendars")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Calendars(CancellationToken ct) => await db.BusinessCalendars.AsNoTracking().Include(c => c.Holidays).OrderBy(c => c.Name)
        .Select(c => new { c.Id, c.Name, c.SiteId, Site = db.Sites.Where(s => s.Id == c.SiteId).Select(s => s.Name).FirstOrDefault(), c.WorkingDays, c.WorkdayStart, c.WorkdayEnd, c.IsDefault, c.RowVersion,
            Holidays = c.Holidays.OrderBy(h => h.Date).Select(h => new { h.Date, h.Name }).ToList() }).ToListAsync(ct);
    [HttpPost("calendars")] [RequirePermission(Permissions.AdminOrganization)] public async Task<object> CreateCalendar(CalendarInput r, CancellationToken ct) => new { (await serviceLevels.SaveCalendarAsync(null, r, ct)).Id };
    [HttpPut("calendars/{id:guid}")] [RequirePermission(Permissions.AdminOrganization)] public async Task<IActionResult> UpdateCalendar(Guid id, CalendarInput r, CancellationToken ct) { await serviceLevels.SaveCalendarAsync(id, r, ct); return NoContent(); }
    [HttpDelete("calendars/{id:guid}")] [RequirePermission(Permissions.AdminOrganization)] public async Task<IActionResult> DeleteCalendar(Guid id, CancellationToken ct) { await serviceLevels.DeleteCalendarAsync(id, ct); return NoContent(); }

    [HttpGet("service-levels")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Policies(CancellationToken ct) => new
    {
        Modules = QualityAnalyticsService.Modules.Select(x => new { x.Key, x.Label, x.Entity, Statuses = Enum.GetNames(x.Status) }),
        Policies = await db.ServiceLevelPolicies.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct)
    };
    [HttpPost("service-levels")] [RequirePermission(Permissions.AdminOrganization)] public async Task<object> CreatePolicy(ServiceLevelInput r, CancellationToken ct) => new { (await serviceLevels.SavePolicyAsync(null, r, ct)).Id };
    [HttpPut("service-levels/{id:guid}")] [RequirePermission(Permissions.AdminOrganization)] public async Task<IActionResult> UpdatePolicy(Guid id, ServiceLevelInput r, CancellationToken ct) { await serviceLevels.SavePolicyAsync(id, r, ct); return NoContent(); }
    [HttpGet("service-levels/status")] [RequirePermission(Permissions.QualityOpsView)] public Task<List<ServiceLevelRow>> ServiceLevelStatus(CancellationToken ct) => serviceLevels.EvaluateAsync(ct);

    // ---------- skip-lot ----------
    [HttpGet("skip-lot")] [RequirePermission(Permissions.InspectionView)]
    public async Task<object> SkipLotRules(CancellationToken ct) => await (await m.ScopeAsync(db.SkipLotRules.AsNoTracking(), Permissions.InspectionView, ct)).OrderBy(x => x.CreatedAt).Select(x => new
    {
        x.Id, x.SiteId, x.PartId, PartNumber = db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).First(), x.SupplierId,
        Supplier = db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.LegalName).FirstOrDefault(), x.QualifyAfterLots, x.InspectEvery, x.RequalifyAfterLots, x.Active,
        x.State, x.ConsecutiveAccepted, x.LotsSinceInspection, x.StateChangedAt, x.RowVersion
    }).ToListAsync(ct);
    [HttpPost("skip-lot")] public async Task<object> CreateSkipLot(SkipLotRuleInput r, CancellationToken ct) => new { (await skipLots.SaveAsync(null, r, ct)).Id };
    [HttpPut("skip-lot/{id:guid}")] public async Task<IActionResult> UpdateSkipLot(Guid id, SkipLotRuleInput r, CancellationToken ct) { await skipLots.SaveAsync(id, r, ct); return NoContent(); }
    [HttpGet("skip-lot/decision")] public Task<SkipLotDecision> SkipLotDecision([FromQuery] Guid siteId, [FromQuery] Guid partId, [FromQuery] Guid? supplierId, CancellationToken ct) => skipLots.DecideAsync(siteId, partId, supplierId, ct);
    [HttpPost("skip-lot/lots")] public async Task<object> RecordLot(LotRecordRequest r, CancellationToken ct) => new { State = await skipLots.RecordLotAsync(r.SiteId, r.PartId, r.SupplierId, r.Lot, r.Inspected, r.Accepted, ct) };
    [HttpGet("skip-lot/{id:guid}/history")]
    public async Task<object> SkipLotHistory(Guid id, CancellationToken ct)
    {
        var rule = await db.SkipLotRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Skip-lot rule");
        await m.RequireAsync(Permissions.InspectionView, rule.SiteId, null, ct);
        return await db.SkipLotEvents.AsNoTracking().Where(e => e.RuleId == id).OrderByDescending(e => e.OccurredAt).Take(500).ToListAsync(ct);
    }

    // ---------- quality reviews ----------
    [HttpGet("reviews")] [RequirePermission(Permissions.QualityOpsView)]
    public async Task<object> Reviews([FromQuery] QualityReviewKind? kind, [FromQuery] QualityReviewStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.QualityReviews.AsNoTracking(), Permissions.QualityOpsView, ct);
        if (kind is not null) query = query.Where(x => x.Kind == kind);
        if (status is not null) query = query.Where(x => x.Status == status);
        return await Paging.PageAsync(query.OrderByDescending(x => x.PeriodTo).Select(x => new { x.Id, x.Number, x.Kind, x.Title, x.Status, x.PeriodFrom, x.PeriodTo, x.MetricsAt,
            PartNumber = db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).FirstOrDefault(), Product = db.Products.Where(p => p.Id == x.ProductId).Select(p => p.Name).FirstOrDefault() }), page, pageSize, ct);
    }
    [HttpPost("reviews")] [RequirePermission(Permissions.QualityOpsEdit)] public async Task<object> CreateReview(QualityReviewInput r, CancellationToken ct) { var x = await reviews.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpGet("reviews/{id:guid}")]
    public async Task<object> Review(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(QualityReviewService.RecordType, id, ct);
        var x = await db.QualityReviews.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.QualityOpsEdit, x.SiteId, x.DepartmentId, ct);
        var draft = x.Status is QualityReviewStatus.Draft or QualityReviewStatus.Rejected;
        return new { Review = x, Metrics = System.Text.Json.JsonDocument.Parse(x.Metrics).RootElement, Owner = Name(x.OwnerId), Actions = new { Edit = canEdit && draft, Refresh = canEdit && draft, Submit = canEdit && draft } };
    }
    [HttpPost("reviews/{id:guid}/refresh")] public async Task<IActionResult> RefreshReview(Guid id, CancellationToken ct) { await reviews.RefreshAsync(id, ct); return NoContent(); }
    [HttpPut("reviews/{id:guid}/text")] public async Task<IActionResult> ReviewText(Guid id, QualityReviewText r, CancellationToken ct) { await reviews.SaveTextAsync(id, r, ct); return NoContent(); }
    [HttpPost("reviews/{id:guid}/submit")] public async Task<IActionResult> SubmitReview(Guid id, CancellationToken ct) { await reviews.SubmitAsync(id, ct); return NoContent(); }

    // ---------- analytics ----------
    [HttpGet("analytics/modules")] public object AnalyticsModules() => QualityAnalyticsService.Modules.Select(x => new { x.Key, x.Label });
    [HttpGet("analytics/aging")] public Task<List<AgingBand>> Aging([FromQuery] string module, CancellationToken ct) => analytics.AgingAsync(module, ct);
    [HttpGet("analytics/bottlenecks")] public Task<List<StatusDwell>> Bottlenecks([FromQuery] string module, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) => analytics.BottlenecksAsync(module, from, to, ct);
    [HttpGet("analytics/cycle-times")] public Task<List<CycleTime>> CycleTimes([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) => analytics.CycleTimesAsync(from, to, ct);
    [HttpGet("analytics/capa-effectiveness")] [RequirePermission(Permissions.CapaView)] public Task<object> CapaEffectiveness([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) => analytics.CapaEffectivenessAsync(from, to, ct);
    [HttpGet("analytics/recurrence")] public Task<List<RecurrenceGroup>> Recurrence(CancellationToken ct) => analytics.RecurrenceAsync(ct);
    [HttpGet("analytics/readiness")] public Task<List<ReadinessScore>> Readiness(CancellationToken ct) => analytics.ReadinessAsync(ct);
    [HttpGet("analytics/supplier-risk")] [RequirePermission(Permissions.SuppliersView)] public Task<List<SupplierRisk>> SupplierRisk(CancellationToken ct) => analytics.SupplierRiskAsync(ct);
}
