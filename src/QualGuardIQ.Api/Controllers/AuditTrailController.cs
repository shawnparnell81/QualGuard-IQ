using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class AuditTrailController(AppDbContext db, IAccessService access, ICurrentUser current) : ControllerBase
{
    public sealed record AuditDto(long Id, DateTimeOffset OccurredAt, string UserName, string Action, string EntityType, Guid EntityId, string Description, string? Changes, string? Reason);
    public sealed record SecurityEventDto(long Id, DateTimeOffset OccurredAt, string EventType, string? UserName, string? IpAddress, bool Succeeded, string? Detail);
    public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize);

    /// <summary>Business audit trail, limited to the sites the viewer may see. Entries are read-only; there is no update or delete endpoint.</summary>
    [HttpGet("audit-trail")]
    [RequirePermission(Permissions.AuditTrailView)]
    public async Task<Page<AuditDto>> AuditTrail([FromQuery] string? entityType, [FromQuery] Guid? entityId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        var scope = await access.GetScopeAsync(current.UserId!.Value, Permissions.AuditTrailView, ct);
        var q = db.AuditEntries.AsNoTracking();
        if (!scope.AllSites)
        {
            var sites = scope.SiteIds.Concat(scope.DepartmentPairs.Select(p => p.Site)).Distinct().ToList();
            q = q.Where(a => a.SiteId != null && sites.Contains(a.SiteId.Value));
        }
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (entityId is not null) q = q.Where(a => a.EntityId == entityId);
        if (from is not null) q = q.Where(a => a.OccurredAt >= from);
        if (to is not null) q = q.Where(a => a.OccurredAt <= to);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditDto(a.Id, a.OccurredAt, a.UserName, a.Action, a.EntityType, a.EntityId, a.Description, a.Changes, a.Reason)).ToListAsync(ct);
        return new Page<AuditDto>(items, total, page, pageSize);
    }

    [HttpGet("security-events")]
    [RequirePermission(Permissions.SecurityLogView)]
    public async Task<Page<SecurityEventDto>> SecurityEvents([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);
        var total = await db.SecurityEvents.CountAsync(ct);
        var items = await db.SecurityEvents.AsNoTracking().OrderByDescending(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(e => new SecurityEventDto(e.Id, e.OccurredAt, e.EventType, e.UserName, e.IpAddress, e.Succeeded, e.Detail)).ToListAsync(ct);
        return new Page<SecurityEventDto>(items, total, page, pageSize);
    }
}
