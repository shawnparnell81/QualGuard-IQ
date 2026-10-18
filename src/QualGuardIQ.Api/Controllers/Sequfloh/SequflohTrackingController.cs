using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Sequfloh;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Sequfloh;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers.Sequfloh;

/// <summary>SEQUFLOH tracking records and tasks: folders, create from a released instruction, data entry, signoffs, task actions, task events, history.</summary>
[ApiController, Authorize, RequirePermission(Permissions.SequflohAccess), SequflohEnabled, Route("api/sequfloh/tracking")]
public sealed class SequflohTrackingController(AppDbContext db, ModuleSupport m, SequflohTrackingService tracking, SequflohPartsService parts) : ControllerBase
{
    /// <summary>Folder listing. folder: validation | fai | repair | production | open | completed | on-hold | rejected | all.</summary>
    [HttpGet]
    public async Task<object> List([FromQuery] string? folder, [FromQuery] string? q, [FromQuery] TrackingStatus? status, [FromQuery] TrackingType? type, [FromQuery] Guid? owner,
        [FromQuery] Guid? assigned, [FromQuery] string? part, [FromQuery] Guid? department, [FromQuery] Guid? site, [FromQuery] string? dateField, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] string? priority, [FromQuery] string? result, [FromQuery] string? sort, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        var query = await tracking.QueryAsync(ct);
        query = (folder ?? "all") switch
        {
            "validation" => query.Where(x => x.Type == TrackingType.Validation),
            "fai" => query.Where(x => x.Type == TrackingType.Fai),
            "repair" => query.Where(x => x.Type == TrackingType.Repair),
            "production" => query.Where(x => x.Type == TrackingType.Production),
            "open" => query.Where(x => SequflohTrackingService.Open.Contains(x.Status)),
            "completed" => query.Where(x => x.Status == TrackingStatus.Completed),
            "on-hold" => query.Where(x => x.Status == TrackingStatus.OnHold),
            "rejected" => query.Where(x => x.Status == TrackingStatus.Rejected),
            _ => query
        };
        if (status is not null) query = query.Where(x => x.Status == status);
        if (type is not null) query = query.Where(x => x.Type == type);
        if (owner is not null) query = query.Where(x => x.OwnerId == owner);
        if (assigned is not null) query = query.Where(x => x.AssignedUserId == assigned);
        if (department is not null) query = query.Where(x => x.DepartmentId == department);
        if (site is not null) query = query.Where(x => x.SiteId == site);
        if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(x => x.Priority == priority);
        if (!string.IsNullOrWhiteSpace(result)) query = query.Where(x => x.Result == result);
        if (!string.IsNullOrWhiteSpace(part)) query = query.Where(x => x.PartNumber != null && EF.Functions.ILike(x.PartNumber, $"%{part.Trim()}%"));
        if (from is not null || to is not null)
        {
            var (f, t) = await m.DayRangeAsync(site, from ?? DateOnly.MinValue.AddYears(2000), to ?? DateOnly.MaxValue.AddYears(-1), ct);
            query = dateField switch
            {
                "due" => query.Where(x => x.DueAt >= f && x.DueAt < t),
                "completed" => query.Where(x => x.ActualCompletion >= f && x.ActualCompletion < t),
                "started" => query.Where(x => x.ActualStart >= f && x.ActualStart < t),
                _ => query.Where(x => x.CreatedAt >= f && x.CreatedAt < t)
            };
        }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.WorkInstructionNumber + " " + x.WorkInstructionTitle + " " + (x.PartNumber ?? "") + " " + (x.SerialNumber ?? "") + " "
                + (x.LotNumber ?? "") + " " + (x.WorkOrder ?? "") + " " + (x.RepairOrder ?? "") + " " + (x.Project ?? ""), like));
        }
        query = (sort ?? "createdAt:desc") switch
        {
            "number:asc" => query.OrderBy(x => x.Number), "number:desc" => query.OrderByDescending(x => x.Number),
            "status:asc" => query.OrderBy(x => x.Status), "status:desc" => query.OrderByDescending(x => x.Status),
            "priority:asc" => query.OrderBy(x => x.Priority), "priority:desc" => query.OrderByDescending(x => x.Priority),
            "dueAt:asc" => query.OrderBy(x => x.DueAt == null).ThenBy(x => x.DueAt), "dueAt:desc" => query.OrderByDescending(x => x.DueAt),
            "partNumber:asc" => query.OrderBy(x => x.PartNumber), "partNumber:desc" => query.OrderByDescending(x => x.PartNumber),
            "actualCompletion:asc" => query.OrderBy(x => x.ActualCompletion), "actualCompletion:desc" => query.OrderByDescending(x => x.ActualCompletion),
            "updatedAt:asc" => query.OrderBy(x => x.UpdatedAt), "updatedAt:desc" => query.OrderByDescending(x => x.UpdatedAt),
            "createdAt:asc" => query.OrderBy(x => x.CreatedAt), _ => query.OrderByDescending(x => x.CreatedAt)
        };
        var now = await tracking.DbNowAsync(ct);
        var paged = await Paging.PageAsync(query.Select(x => new
        {
            x.Id, x.Number, x.Type, x.Status, x.Result, x.ApprovalStatus, x.Priority, x.WorkInstructionId, x.WorkInstructionNumber, x.WorkInstructionRevision, x.WorkInstructionTitle,
            x.PartNumber, x.PartDescription, x.SerialNumber, x.LotNumber, x.WorkOrder, x.RepairOrder, x.Operation, x.PlannedStart, x.DueAt, x.ActualStart, x.ActualCompletion, x.CreatedAt, x.UpdatedAt,
            x.ActiveSeconds, x.PausedSeconds, x.HoldSeconds, x.CurrentStateStartedAt, x.MinBenchmarkMinutes, x.MaxBenchmarkMinutes, x.VarianceMinutes, x.StateVersion, x.AssignedUserId,
            Owner = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).FirstOrDefault(),
            AssignedUser = db.Users.Where(u => u.Id == x.AssignedUserId).Select(u => u.DisplayName).FirstOrDefault(),
            Department = db.Departments.Where(d => d.Id == x.DepartmentId).Select(d => d.Name).FirstOrDefault(),
            Site = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).FirstOrDefault()
        }), page, pageSize, ct);
        return new
        {
            Items = paged.Items.Select(x => new
            {
                x.Id, x.Number, x.Type, x.Status, x.Result, x.ApprovalStatus, x.Priority, x.WorkInstructionId, x.WorkInstructionNumber, x.WorkInstructionRevision, x.WorkInstructionTitle,
                x.PartNumber, x.PartDescription, x.SerialNumber, x.LotNumber, x.WorkOrder, x.RepairOrder, x.Operation, x.PlannedStart, x.DueAt, x.ActualStart, x.ActualCompletion, x.CreatedAt, x.UpdatedAt,
                x.MinBenchmarkMinutes, x.MaxBenchmarkMinutes, x.VarianceMinutes, x.StateVersion, x.AssignedUserId, x.Owner, x.AssignedUser, x.Department, x.Site,
                ElapsedSeconds = x.ActiveSeconds + (x.Status == TrackingStatus.InProgress && x.CurrentStateStartedAt != null ? Math.Max(0, (long)(now - x.CurrentStateStartedAt.Value).TotalSeconds) : 0),
                PausedSeconds = x.PausedSeconds + (x.Status == TrackingStatus.Paused && x.CurrentStateStartedAt != null ? Math.Max(0, (long)(now - x.CurrentStateStartedAt.Value).TotalSeconds) : 0),
                HoldSeconds = x.HoldSeconds + (x.Status == TrackingStatus.OnHold && x.CurrentStateStartedAt != null ? Math.Max(0, (long)(now - x.CurrentStateStartedAt.Value).TotalSeconds) : 0),
            }),
            paged.Total, ServerTime = now
        };
    }

    /// <summary>Released work instructions a record of this type can execute (the exact revision is stored on the record).</summary>
    [HttpGet("instructions")]
    public async Task<object> Instructions([FromQuery] TrackingType type, [FromQuery] string? q, [FromQuery] Guid? part, CancellationToken ct)
    {
        var types = SequflohTrackingService.InstructionTypes(type);
        var query = (await m.ScopeAsync(db.SequflohWorkInstructions.AsNoTracking(), Permissions.SequflohAccess, ct)).Where(x => x.Status == WorkInstructionStatus.Released && types.Contains(x.Type));
        if (part is not null) query = query.Where(x => x.PartId == part || x.PartId == null);
        if (!string.IsNullOrWhiteSpace(q)) { var like = $"%{q.Trim()}%"; query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + (x.PartNumber ?? ""), like)); }
        return await query.OrderBy(x => x.Number).Take(50).Select(x => new { x.Id, x.Number, x.Revision, x.Title, x.Type, x.PartId, x.PartNumber, x.PartDescription, x.Operation, x.RepairDescription, x.SiteId, x.DepartmentId }).ToListAsync(ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        var x = await tracking.LoadAsync(id, false, ct);
        var now = await tracking.DbNowAsync(ct);
        var (active, paused, hold) = SequflohTrackingService.Durations(x, now);
        string? Name(Guid? u) => u is null ? null : db.Users.Where(y => y.Id == u).Select(y => y.DisplayName).FirstOrDefault();
        var events = await db.SequflohTaskEvents.AsNoTracking().Where(e => e.TrackingRecordId == id).OrderBy(e => e.CreatedAt)
            .Select(e => new { e.Id, e.EventType, e.FromStatus, e.ToStatus, e.CreatedAt, e.UserName, e.Reason, e.Comment, e.ActiveSeconds, e.PausedSeconds, e.HoldSeconds, e.OperationId,
                AssignedBefore = db.Users.Where(u => u.Id == e.AssignedBeforeId).Select(u => u.DisplayName).FirstOrDefault(),
                AssignedAfter = db.Users.Where(u => u.Id == e.AssignedAfterId).Select(u => u.DisplayName).FirstOrDefault() }).ToListAsync(ct);
        var canEnter = (x.Status is TrackingStatus.InProgress or TrackingStatus.PendingInspection) && await m.CanAsync(Permissions.SequflohStartTask, x.SiteId, x.DepartmentId, ct);
        return new
        {
            Record = new
            {
                x.Id, x.Number, x.Type, x.WorkInstructionId, x.WorkInstructionNumber, x.WorkInstructionRevision, x.WorkInstructionTitle, x.PartId, x.PartNumber, x.PartDescription, x.SerialNumber, x.LotNumber,
                x.WorkOrder, x.RepairOrder, x.CustomerReturnReference, x.Project, x.Operation, x.OwnerId, x.AssignedUserId, x.DepartmentId, x.SiteId, x.Status, x.Priority, x.PlannedStart, x.DueAt,
                x.ActualStart, x.ActualCompletion, x.CurrentStateStartedAt, x.StateVersion, x.LastTransitionId, x.LastTransitionAt, x.MinBenchmarkMinutes, x.MaxBenchmarkMinutes, x.VarianceMinutes,
                x.VarianceReason, x.Result, x.ApprovalStatus, x.QualityReviewRequired, x.EngineeringReviewRequired, x.QualityReviewedAt, x.EngineeringReviewedAt, x.EngineeringConclusion,
                x.EvidenceReferences, x.TechnicianComments, x.QualityComments, x.EngineeringComments, x.Deviations, x.NonconformanceReferences, x.PauseReason, x.HoldReason, x.CancelReason,
                x.FailureReason, x.RejectionReason, x.ReportedCondition, x.ObservedCondition, x.RepairDescription, x.FinalDisposition, x.FaiType, x.CreatedAt, x.UpdatedAt,
                Details = JsonNode.Parse(x.Details), Steps = JsonNode.Parse(x.Steps), Characteristics = JsonNode.Parse(x.Characteristics), PartsRows = JsonNode.Parse(x.PartsRows), Signoffs = JsonNode.Parse(x.Signoffs),
                ElapsedSeconds = active, PausedSeconds = paused, HoldSeconds = hold,
                Owner = Name(x.OwnerId), AssignedUser = Name(x.AssignedUserId), ActiveOperator = Name(x.ActiveOperatorId), CompletedBy = Name(x.CompletedById),
                QualityReviewedBy = Name(x.QualityReviewedById), EngineeringReviewedBy = Name(x.EngineeringReviewedById), CreatedBy = Name(x.CreatedById), UpdatedBy = Name(x.UpdatedById),
                Site = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code + " · " + s.Name).FirstOrDefault(),
                Department = db.Departments.Where(d => d.Id == x.DepartmentId).Select(d => d.Name).FirstOrDefault()
            },
            Events = events, ServerTime = now, CanEnter = canEnter,
            CanPlan = !SequflohTrackingService.Terminal.Contains(x.Status) && x.Status != TrackingStatus.PendingReview
                && (await m.CanAsync(SequflohTrackingService.CreatePermission(x.Type), x.SiteId, x.DepartmentId, ct) || await m.CanAsync(Permissions.SequflohDispatchTask, x.SiteId, x.DepartmentId, ct)),
            CanQualityComment = x.Status is not (TrackingStatus.Completed or TrackingStatus.Canceled) && await m.CanAsync(Permissions.SequflohEditQualityFolder, x.SiteId, x.DepartmentId, ct),
            CanEngineeringComment = x.Status is not (TrackingStatus.Completed or TrackingStatus.Canceled) && await m.CanAsync(Permissions.SequflohEditEngineeringFolder, x.SiteId, x.DepartmentId, ct),
            Actions = await tracking.AvailableActionsAsync(x, ct),
            CompletionProblems = await tracking.CompletionProblemsAsync(x, ct)
        };
    }

    [HttpPost]
    public async Task<object> Create(TrackingCreateInput r, CancellationToken ct) { var x = await tracking.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpPut("{id:guid}")]
    public async Task<object> Save(Guid id, TrackingSaveInput r, CancellationToken ct) { var x = await tracking.SaveAsync(id, r, ct); return new { x.Id, x.StateVersion, x.Result }; }
    [HttpPost("{id:guid}/actions/{name}")]
    public async Task<object> Act(Guid id, string name, TaskActionInput r, CancellationToken ct) { var x = await tracking.ActAsync(id, name, r, ct); return new { x.Id, x.Status, x.StateVersion }; }
    [HttpPost("{id:guid}/signoffs")]
    public async Task<object> Sign(Guid id, SignoffInput r, CancellationToken ct) { var x = await tracking.SignAsync(id, r, ct); return new { x.Id, x.StateVersion }; }
    [HttpGet("{id:guid}/history")]
    public async Task<object> History(Guid id, CancellationToken ct) { await tracking.LoadAsync(id, false, ct); return await parts.HistoryAsync(SequflohTrackingService.EntityType, id, ct); }
}
