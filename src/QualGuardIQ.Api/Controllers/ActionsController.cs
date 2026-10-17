using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/actions")]
public sealed class ActionsController(ActionService actions) : ControllerBase
{
    public sealed record CompleteRequest(string Notes);
    public sealed record VerifyRequest(bool Effective, string Notes);

    internal static async Task<List<RecordsController.ActionDto>> ToDtos(AppDbContext db, IEnumerable<ActionItem> list, ISiteCalendar calendar, CancellationToken ct)
    {
        var items = list.ToList();
        var ids = items.Select(a => a.AssigneeId).Distinct().ToList();
        var names = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var today = new Dictionary<Guid, DateOnly>();
        foreach (var site in items.Select(a => a.SiteId).Distinct()) today[site] = await calendar.TodayAsync(site, ct);
        return items.Select(a => new RecordsController.ActionDto(a.Id, a.Number, a.Title, a.Description, a.ActionType, a.AssigneeId,
            names.GetValueOrDefault(a.AssigneeId, ""), a.DueDate, a.Status, a.RequiresVerification, a.CompletionNotes, a.VerificationNotes,
            a.Status is ActionStatus.Open or ActionStatus.InProgress && a.DueDate < today[a.SiteId], a.CompletedById)).ToList();
    }

    [HttpPost("{id:guid}/complete")]
    public async Task<IActionResult> Complete(Guid id, CompleteRequest req, CancellationToken ct)
    {
        await actions.CompleteAsync(id, req.Notes, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/verify")]
    public async Task<IActionResult> Verify(Guid id, VerifyRequest req, CancellationToken ct)
    {
        await actions.VerifyAsync(id, req.Effective, req.Notes, ct);
        return NoContent();
    }
}

/// <summary>Personal work queue: assigned actions, approvals waiting on me, overdue items, notifications.</summary>
[ApiController]
[Authorize]
[Route("api/my")]
public sealed class MyWorkController(AppDbContext db, ICurrentUser current, WorkflowService workflow) : ControllerBase
{
    public sealed record SignRequest(bool Approve, string Password, string? Comment, string? MfaCode = null);

    private ISiteCalendar Cal => HttpContext.RequestServices.GetRequiredService<ISiteCalendar>();

    [HttpGet("work")]
    public async Task<IActionResult> Work(CancellationToken ct)
    {
        var uid = current.UserId!.Value;
        var mine = await db.ActionItems.AsNoTracking().Where(a => a.AssigneeId == uid && (a.Status == ActionStatus.Open || a.Status == ActionStatus.InProgress))
            .OrderBy(a => a.DueDate).ToListAsync(ct);
        var waitingVerification = await db.ActionItems.AsNoTracking().Where(a => a.CreatedById == uid && a.Status == ActionStatus.Completed && a.RequiresVerification).ToListAsync(ct);
        var approvals = await workflow.MyPendingAsync(ct);
        var submitted = await db.ApprovalRequests.AsNoTracking().Where(a => a.SubmittedById == uid && a.Status == ApprovalStatus.Pending).ToListAsync(ct);
        var actionDtos = await ActionsController.ToDtos(db, mine, Cal, ct);
        return Ok(new
        {
            assigned = actionDtos,
            overdue = actionDtos.Where(a => a.IsOverdue),
            toVerify = await ActionsController.ToDtos(db, waitingVerification, Cal, ct),
            approvals = approvals.Select(a => new { a.Request.Id, a.Request.RecordType, a.Request.RecordId, a.Request.RecordNumber, a.StepName, submittedAt = a.Request.CreatedAt }),
            waitingOnOthers = submitted.Select(a => new { a.Id, a.RecordType, a.RecordId, a.RecordNumber, submittedAt = a.CreatedAt })
        });
    }

    [HttpPost("approvals/{id:guid}/sign")]
    public async Task<IActionResult> Sign(Guid id, SignRequest req, CancellationToken ct)
    {
        var r = await workflow.DecideAsync(id, req.Approve, req.Password, req.Comment, req.MfaCode, ct);
        return Ok(new { r.Status, r.CurrentStep });
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] bool unreadOnly, CancellationToken ct)
    {
        var uid = current.UserId!.Value;
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == uid);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        var items = await q.OrderByDescending(n => n.Id).Take(100).Select(n => new { n.Id, n.Title, n.Body, n.RecordType, n.RecordId, n.Link, n.CreatedAt, n.ReadAt }).ToListAsync(ct);
        var unread = await db.Notifications.CountAsync(n => n.UserId == uid && n.ReadAt == null, ct);
        return Ok(new { unread, items });
    }

    [HttpPost("notifications/{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id, CancellationToken ct)
    {
        var uid = current.UserId!.Value;
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct) ?? throw new NotFoundException("Notification");
        n.ReadAt ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        var uid = current.UserId!.Value;
        await db.Notifications.Where(n => n.UserId == uid && n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, DateTimeOffset.UtcNow), ct);
        return NoContent();
    }
}

[ApiController]
[Authorize]
[Route("api/search")]
public sealed class SearchController(QualGuardIQ.Infrastructure.Records.SearchService search, ICurrentUser current) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string q, [FromQuery] string? type, [FromQuery] int limit = 25, CancellationToken ct = default) =>
        Ok(await search.SearchAsync(current.UserId!.Value, q ?? "", type, limit, ct));
}

[ApiController]
[Authorize]
[Route("api/users/lookup")]
public sealed class UserLookupController(AppDbContext db) : ControllerBase
{
    /// <summary>Minimal active-user list for assignee pickers.</summary>
    [HttpGet]
    public async Task<IActionResult> Lookup(CancellationToken ct) =>
        Ok(await db.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName).Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct));
}
