using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Risks;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController, Authorize, Route("api/risks")]
public sealed class RisksController(AppDbContext db, RiskService svc, ModuleSupport m) : ControllerBase
{
    [HttpGet("settings")] [RequirePermission(Permissions.RisksView)] public Task<RiskSettingsDto> Settings(CancellationToken ct) => svc.GetSettingsAsync(ct);
    [HttpPut("settings")] [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> SaveSettings(RiskSettingsDto r, CancellationToken ct) { await svc.SaveSettingsAsync(r, ct); return NoContent(); }

    [HttpGet]
    [RequirePermission(Permissions.RisksView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] RiskStatus? status, [FromQuery] RiskKind? kind, [FromQuery] string? category, [FromQuery] string? band,
        [FromQuery] bool openOnly = false, [FromQuery] bool reviewDue = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Risks.AsNoTracking(), Permissions.RisksView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (kind is not null) query = query.Where(x => x.Kind == kind);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(band)) query = query.Where(x => (x.ResidualBand ?? x.InitialBand) == band);
        if (openOnly) query = query.Where(x => x.Status != RiskStatus.Closed && x.Status != RiskStatus.Cancelled);
        if (reviewDue) { var today = await m.TodayAsync(null, ct); query = query.Where(x => x.Status != RiskStatus.Closed && x.Status != RiskStatus.Cancelled && x.ReviewDueOn <= today.AddDays(14)); }
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Title + " " + x.Category + " " + (x.Process ?? ""), $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderByDescending(x => x.ResidualScore ?? x.InitialScore).ThenBy(x => x.Number).Select(x => new
        {
            x.Id, x.Number, x.Kind, x.Category, x.Title, x.Status, x.InitialScore, x.InitialBand, x.ResidualScore, x.ResidualBand, x.ReviewDueOn, x.Escalated,
            x.Probability, x.Severity, x.ResidualProbability, x.ResidualSeverity,
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    /// <summary>Counts of open risks per probability × severity cell, for the heat map (current rating = residual when assessed).</summary>
    [HttpGet("matrix")]
    [RequirePermission(Permissions.RisksView)]
    public async Task<object> Matrix([FromQuery] bool residual = true, CancellationToken ct = default)
    {
        var query = (await m.ScopeAsync(db.Risks.AsNoTracking(), Permissions.RisksView, ct)).Where(x => x.Status != RiskStatus.Closed && x.Status != RiskStatus.Cancelled);
        var rows = await query.Select(x => new { P = residual && x.ResidualProbability != null ? x.ResidualProbability.Value : x.Probability, S = residual && x.ResidualSeverity != null ? x.ResidualSeverity.Value : x.Severity })
            .GroupBy(x => new { x.P, x.S }).Select(g => new { Probability = g.Key.P, Severity = g.Key.S, Count = g.Count() }).ToListAsync(ct);
        return new { Settings = await svc.GetSettingsAsync(ct), Cells = rows };
    }

    [HttpPost] [RequirePermission(Permissions.RisksEdit)]
    public async Task<object> Create(RiskInput r, CancellationToken ct) { var x = await svc.CreateAsync(r, ct); return new { x.Id, x.Number }; }
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, RiskInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RiskService.RecordType, id, ct);
        var x = await db.Risks.AsNoTracking().FirstAsync(e => e.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.RisksEdit, x.SiteId, x.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.RisksApprove, x.SiteId, x.DepartmentId, ct);
        var reviews = await db.RiskReviews.AsNoTracking().Where(r => r.RiskId == id).OrderByDescending(r => r.ReviewedOn).ThenByDescending(r => r.CreatedAt)
            .Select(r => new { r.Id, r.ReviewedOn, r.Score, r.Band, r.Notes, r.StillEffective, ReviewedByName = db.Users.Where(u => u.Id == r.ReviewedById).Select(u => u.DisplayName).First() }).ToListAsync(ct);
        var s = x.Status;
        var active = s is RiskStatus.Open or RiskStatus.Mitigating or RiskStatus.Monitoring;
        return new
        {
            x.Id, x.Number, x.Kind, x.Category, x.Source, x.Title, x.Description, x.Cause, x.Consequence, x.Process, x.ProductId, x.SiteId, x.DepartmentId, x.OwnerId,
            x.Probability, x.Severity, x.Detectability, x.InitialScore, x.InitialBand, x.ExistingControls, x.MitigationPlan,
            x.ResidualProbability, x.ResidualSeverity, x.ResidualDetectability, x.ResidualScore, x.ResidualBand, x.AcceptanceCriteria, x.AcceptedAt,
            x.ReviewDueOn, x.ReviewIntervalDays, x.LastReviewedOn, x.Monitoring, x.Escalated, x.Status, x.ClosureNotes, x.ClosedAt, x.RowVersion, x.CreatedAt,
            OwnerName = await db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            AcceptedByName = x.AcceptedById == null ? null : await db.Users.Where(u => u.Id == x.AcceptedById).Select(u => u.DisplayName).FirstAsync(ct),
            ProductName = x.ProductId == null ? null : await db.Products.Where(p => p.Id == x.ProductId).Select(p => p.Name).FirstAsync(ct),
            SiteCode = await db.Sites.Where(e => e.Id == x.SiteId).Select(e => e.Code).FirstAsync(ct),
            Reviews = reviews, Settings = await svc.GetSettingsAsync(ct),
            Allowed = Allowed.Of(("edit", canEdit && active), ("mitigate", canEdit && s == RiskStatus.Open && x.MitigationPlan != null),
                ("residual", canEdit && active && await Allowed.ActionsClosedAsync(db, RiskService.RecordType, id, false, ct)),
                ("review", canEdit && active), ("close", canEdit && active), ("cancel", canEdit && active),
                ("reopen", canApprove && s is RiskStatus.Closed or RiskStatus.Cancelled), ("actions", canEdit && active))
        };
    }

    [HttpPost("{id:guid}/mitigate")] public async Task<IActionResult> Mitigate(Guid id, CancellationToken ct) { await svc.StartMitigationAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/residual")] public async Task<IActionResult> Residual(Guid id, ResidualInput r, CancellationToken ct) { await svc.AssessResidualAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/reviews")] public async Task<object> Review(Guid id, RiskReviewInput r, CancellationToken ct) => new { (await svc.ReviewAsync(id, r, ct)).Id };
    [HttpPost("{id:guid}/close")] public async Task<IActionResult> Close(Guid id, TextRequest r, CancellationToken ct) { await svc.CloseAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, TextRequest r, CancellationToken ct) { await svc.CancelAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("{id:guid}/reopen")] public async Task<IActionResult> Reopen(Guid id, TextRequest r, CancellationToken ct) { await svc.ReopenAsync(id, r.Text, ct); return NoContent(); }
}
