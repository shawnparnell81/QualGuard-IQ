using QualGuardIQ.Domain.Audit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Engineering;

public sealed record AccessReviewInput(string Title, AccessReviewKind Kind, string? Findings);
public sealed record AccessDecisionInput(AccessDecision Decision, string? Comment);
public sealed record DeploymentInput(string Version, DeploymentScope Scope, List<Guid> SiteIds, DateOnly? PlannedOn, string? Notes);

/// <summary>
/// Security hardening governance: security reviews and risk assessments (findings), privileged access reviews and access certification
/// (a snapshot of role assignments, each kept or revoked; revocations are applied on completion, audited and logged as security events),
/// and the deployment/rollout history.
/// </summary>
public sealed class SecurityGovernanceService(AppDbContext db, ModuleSupport m, INumberingService numbers, UserManager<ApplicationUser> users, ISecurityEventLogger security, ICurrentUser current, IClock clock)
{
    static readonly string[] PrivilegedPermissions = [Permissions.AdminUsers, Permissions.AdminRoles, Permissions.AdminOrganization, Permissions.ApiAdmin, Permissions.SecurityReview];

    public async Task<AccessReview> CreateAsync(AccessReviewInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = new AccessReview { Number = await numbers.NextAsync("AccessReview", null, ct), Title = r.Title.Trim(), Kind = r.Kind, Findings = r.Findings?.Trim(), ReviewerId = m.UserId };
        db.AccessReviews.Add(a);
        if (r.Kind is AccessReviewKind.PrivilegedAccessReview or AccessReviewKind.AccessCertification)
        {
            var rows = await (from x in db.UserRoleAssignments.AsNoTracking()
                              join u in db.Users.AsNoTracking() on x.UserId equals u.Id
                              join ro in db.QmsRoles.AsNoTracking() on x.RoleId equals ro.Id
                              where u.IsActive && u.PortalKind == null
                              select new { x.Id, x.UserId, u.DisplayName, u.Email, Role = ro.Name, x.SiteId, x.DepartmentId, Privileged = ro.IsSystem || db.Set<RolePermission>().Any(p => p.RoleId == ro.Id && PrivilegedPermissions.Contains(p.Permission)) }).ToListAsync(ct);
            if (r.Kind == AccessReviewKind.PrivilegedAccessReview) rows = rows.Where(x => x.Privileged).ToList();
            var sites = await db.Sites.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, ct);
            db.AccessReviewItems.AddRange(rows.Select(x => new AccessReviewItem { ReviewId = a.Id, AssignmentId = x.Id, UserId = x.UserId, UserName = $"{x.DisplayName} <{x.Email}>", RoleName = x.Role,
                Scope = x.SiteId is { } s ? sites.GetValueOrDefault(s) ?? "site" : "All sites", Privileged = x.Privileged }));
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return a;
    }

    public async Task<List<AccessReview>> ListAsync(CancellationToken ct) => await db.AccessReviews.AsNoTracking().OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct);

    public async Task<object> GetAsync(Guid id, CancellationToken ct)
    {
        var a = await db.AccessReviews.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Access review");
        var items = await db.AccessReviewItems.AsNoTracking().Where(i => i.ReviewId == id).OrderBy(i => i.UserName).ThenBy(i => i.RoleName).ToListAsync(ct);
        return new { Review = a, Kind = a.Kind.ToString(), Status = a.Status.ToString(), Items = items, Pending = items.Count(i => i.Decision == AccessDecision.Pending), Revoke = items.Count(i => i.Decision == AccessDecision.Revoke) };
    }

    public async Task DecideAsync(Guid id, Guid itemId, AccessDecisionInput r, CancellationToken ct)
    {
        var a = await db.AccessReviews.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Access review");
        if (a.Status != AccessReviewStatus.Open) throw new ConflictException("The review is completed.");
        var i = await db.AccessReviewItems.FirstOrDefaultAsync(x => x.Id == itemId && x.ReviewId == id, ct) ?? throw new NotFoundException("Item");
        if (r.Decision == AccessDecision.Revoke && string.IsNullOrWhiteSpace(r.Comment)) throw new AppException("Give the reason for revoking access.");
        if (r.Decision == AccessDecision.Revoke && i.UserId == m.UserId) throw new AppException("You cannot revoke your own access in a review; another reviewer must decide.");
        i.Decision = r.Decision; i.Comment = r.Comment?.Trim();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Completing a certification needs every item decided; revocations remove the role assignment (needs admin.users) and invalidate the user's sessions.</summary>
    public async Task CompleteAsync(Guid id, string? findings, CancellationToken ct)
    {
        var a = await db.AccessReviews.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Access review");
        if (a.Status != AccessReviewStatus.Open) throw new ConflictException("The review is completed.");
        var items = await db.AccessReviewItems.Where(i => i.ReviewId == id).ToListAsync(ct);
        if (items.Any(i => i.Decision == AccessDecision.Pending)) throw new AppException($"{items.Count(i => i.Decision == AccessDecision.Pending)} assignment(s) still need a keep or revoke decision.");
        if (items.Count == 0 && string.IsNullOrWhiteSpace(findings ?? a.Findings)) throw new AppException("Record the review findings.");
        var revoke = items.Where(i => i.Decision == AccessDecision.Revoke).ToList();
        if (revoke.Count > 0 && !await m.CanAnyAsync(Permissions.AdminUsers, ct)) throw new ForbiddenException("Revoking access needs the user administration permission.");
        foreach (var i in revoke)
        {
            var asg = await db.UserRoleAssignments.FirstOrDefaultAsync(x => x.Id == i.AssignmentId, ct);
            if (asg == null) continue;
            db.UserRoleAssignments.Remove(asg);
            db.AuditEntries.Add(new AuditEntry { OccurredAt = clock.UtcNow, UserId = current.UserId, UserName = current.UserName, Action = "RoleRemoved", EntityType = "User", EntityId = i.UserId,
                Description = $"Access review {a.Number}: removed role {i.RoleName} ({i.Scope}) from {i.UserName}. Reason: {i.Comment}", CorrelationId = current.CorrelationId });
        }
        a.Status = AccessReviewStatus.Completed; a.CompletedAt = m.Now; if (findings is { Length: > 0 }) a.Findings = findings.Trim();
        await db.SaveChangesAsync(ct);
        foreach (var uid in revoke.Select(i => i.UserId).Distinct())
            if (await users.FindByIdAsync(uid.ToString()) is { } u) await users.UpdateSecurityStampAsync(u);
        await security.LogAsync("AccessReviewCompleted", true, current.UserId, current.UserName, $"{a.Number}: {items.Count} assignments certified, {revoke.Count} revoked", ct);
    }

    // ---------- deployments ----------
    public async Task<List<DeploymentRelease>> DeploymentsAsync(CancellationToken ct) => await db.DeploymentReleases.AsNoTracking().OrderByDescending(d => d.CreatedAt).Take(200).ToListAsync(ct);

    public async Task<DeploymentRelease> PlanDeploymentAsync(DeploymentInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Version, "Version", 60);
        var sites = r.SiteIds.Distinct().ToList();
        var known = await db.Sites.CountAsync(s => sites.Contains(s.Id), ct);
        if (known != sites.Count) throw new AppException("Select existing sites.");
        var total = await db.Sites.CountAsync(s => !s.IsArchived, ct);
        switch (r.Scope)
        {
            case DeploymentScope.SinglePlant when sites.Count != 1: throw new AppException("A single plant deployment covers exactly one site.");
            case DeploymentScope.MultiPlant or DeploymentScope.Regional when sites.Count < 1: throw new AppException("Select the sites in the rollout.");
            case DeploymentScope.Global: sites = await db.Sites.Where(s => !s.IsArchived).Select(s => s.Id).ToListAsync(ct); break;
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var d = new DeploymentRelease { Number = await numbers.NextAsync("Deployment", null, ct), Version = r.Version.Trim(), Scope = r.Scope, SiteIds = sites, PlannedOn = r.PlannedOn, Notes = r.Notes?.Trim() };
        db.DeploymentReleases.Add(d);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return d;
    }

    public async Task SetDeploymentStatusAsync(Guid id, DeploymentStatus status, string? notes, CancellationToken ct)
    {
        var d = await db.DeploymentReleases.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Deployment");
        if (d.Status is DeploymentStatus.Completed or DeploymentStatus.RolledBack && status != DeploymentStatus.RolledBack) throw new ConflictException($"The deployment is {d.Status}.");
        if (status == DeploymentStatus.RolledBack && string.IsNullOrWhiteSpace(notes)) throw new AppException("Describe why the deployment was rolled back.");
        d.Status = status;
        if (status == DeploymentStatus.Completed) d.DeployedOn = DateOnly.FromDateTime(m.Now.UtcDateTime);
        if (notes is { Length: > 0 }) d.Notes = string.IsNullOrEmpty(d.Notes) ? notes.Trim() : d.Notes + "\n" + notes.Trim();
        await db.SaveChangesAsync(ct);
    }
}
