using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[RequirePermission(Permissions.AdminUsers)]
public sealed class UsersController(AppDbContext db, UserManager<ApplicationUser> users, ICurrentUser current, IClock clock, ISecurityEventLogger security, SignInSecurityService mfa) : ControllerBase
{
    public sealed record UserDto(Guid Id, string Email, string DisplayName, bool IsActive, bool IsLockedOut, DateTimeOffset? LastSignInAt, IReadOnlyList<AssignmentDto> Roles,
        bool MfaEnabled = false, bool MustChangePassword = false, DateTimeOffset? PasswordChangedAt = null);
    public sealed record AssignmentDto(Guid Id, Guid RoleId, string RoleName, Guid? SiteId, Guid? DepartmentId);
    /// <summary>MustChangePassword (default true) makes the first sign-in ask for a new password before anything else.</summary>
    public sealed record CreateUserRequest(string Email, string DisplayName, string Password, bool? MustChangePassword = null);
    public sealed record TemporaryPasswordRequest(string Password, bool MustChangePassword = true);
    public sealed record ResetMfaRequest(string Reason);
    public sealed record AssignRoleRequest(Guid RoleId, Guid? SiteId, Guid? DepartmentId);

    public sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
    {
        public CreateUserValidator()
        {
            RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
        }
    }

    [HttpGet]
    public async Task<IReadOnlyList<UserDto>> List(CancellationToken ct)
    {
        var list = await db.Users.OrderBy(u => u.DisplayName).ToListAsync(ct);
        var assignments = await db.UserRoleAssignments.Include(a => a.Role).ToListAsync(ct);
        var now = clock.UtcNow;
        return list.Select(u => new UserDto(u.Id, u.Email!, u.DisplayName, u.IsActive, u.LockoutEnd > now, u.LastSignInAt,
            assignments.Where(a => a.UserId == u.Id).Select(a => new AssignmentDto(a.Id, a.RoleId, a.Role!.Name, a.SiteId, a.DepartmentId)).ToList(),
            u.MfaSecret != null, u.MustChangePassword, u.PasswordChangedAt)).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(CreateUserRequest req, [FromServices] IValidator<CreateUserRequest> validator)
    {
        await validator.ValidateAndThrowAsync(req);
        var user = new ApplicationUser { UserName = req.Email, Email = req.Email, EmailConfirmed = true, DisplayName = req.DisplayName, CreatedAt = clock.UtcNow, MustChangePassword = req.MustChangePassword ?? true };
        var result = await users.CreateAsync(user, req.Password);
        if (!result.Succeeded) throw new AppException(string.Join(" ", result.Errors.Select(e => e.Description)));
        await WriteAudit("Created", user, $"Created user {user.Email}{(user.MustChangePassword ? " with a temporary password (a new password is required at first sign-in)" : "")}");
        return Created($"api/admin/users/{user.Id}", user.Id);
    }

    [HttpPost("{id:guid}/roles")]
    public async Task<ActionResult<Guid>> AssignRole(Guid id, AssignRoleRequest req, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User");
        if (user.PortalKind is not null) throw new AppException("Portal accounts cannot hold staff roles; they only reach the external portal.");
        var role = await db.QmsRoles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == req.RoleId, ct) ?? throw new NotFoundException("Role");
        if (req.DepartmentId is not null && req.SiteId is null) throw new AppException("A department scope requires a site.");
        if (req.SiteId is { } s && !await db.Sites.AnyAsync(x => x.Id == s, ct)) throw new NotFoundException("Site");
        if (req.DepartmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d && x.SiteId == req.SiteId, ct)) throw new NotFoundException("Department at that site");
        if (req.SiteId is not null && role.Permissions.Any(p => Permissions.GlobalOnly.Contains(p.Permission)))
            throw new AppException("This role includes administration permissions and can only be assigned organization-wide.");
        if (await db.UserRoleAssignments.AnyAsync(a => a.UserId == id && a.RoleId == req.RoleId && a.SiteId == req.SiteId && a.DepartmentId == req.DepartmentId, ct))
            throw new ConflictException("The user already has this role for that scope.");

        var a = new UserRoleAssignment { UserId = id, RoleId = role.Id, SiteId = req.SiteId, DepartmentId = req.DepartmentId };
        db.UserRoleAssignments.Add(a);
        await db.SaveChangesAsync(ct);
        await users.UpdateSecurityStampAsync(user);
        await WriteAudit("RoleAssigned", user, $"Assigned role {role.Name} to {user.Email}{ScopeText(req.SiteId, req.DepartmentId)}");
        return a.Id;
    }

    [HttpDelete("{id:guid}/roles/{assignmentId:guid}")]
    public async Task<IActionResult> RemoveRole(Guid id, Guid assignmentId, CancellationToken ct)
    {
        var a = await db.UserRoleAssignments.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == assignmentId && x.UserId == id, ct) ?? throw new NotFoundException("Role assignment");
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User");
        if (id == current.UserId && a.Role!.IsSystem) throw new AppException("You cannot remove your own administrator role.");
        db.UserRoleAssignments.Remove(a);
        await db.SaveChangesAsync(ct);
        await users.UpdateSecurityStampAsync(user);
        await WriteAudit("RoleRemoved", user, $"Removed role {a.Role!.Name} from {user.Email}{ScopeText(a.SiteId, a.DepartmentId)}");
        return NoContent();
    }

    /// <summary>Disabling a user ends all of their sessions immediately.</summary>
    [HttpPost("{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id)
    {
        if (id == current.UserId) throw new AppException("You cannot disable your own account.");
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User");
        user.IsActive = false;
        await users.UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user);
        await security.LogAsync("AccountDisabled", true, user.Id, user.Email, $"By {current.UserName}");
        await WriteAudit("Disabled", user, $"Disabled user {user.Email}");
        return NoContent();
    }

    [HttpPost("{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User");
        user.IsActive = true;
        await users.UpdateAsync(user);
        await WriteAudit("Enabled", user, $"Enabled user {user.Email}");
        return NoContent();
    }

    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User");
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await security.LogAsync("AccountUnlocked", true, user.Id, user.Email, $"By {current.UserName}");
        await WriteAudit("Unlocked", user, $"Unlocked user {user.Email}");
        return NoContent();
    }

    /// <summary>Signs the user out of every session.</summary>
    [HttpPost("{id:guid}/revoke-sessions")]
    public async Task<IActionResult> RevokeSessions(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User");
        await users.UpdateSecurityStampAsync(user);
        await security.LogAsync("SessionsRevoked", true, user.Id, user.Email, $"By {current.UserName}");
        return NoContent();
    }

    /// <summary>Sets a password for the user (a temporary one by default: the next sign-in must choose a new password). Ends all of their sessions.</summary>
    [HttpPost("{id:guid}/temporary-password")]
    public async Task<IActionResult> SetTemporaryPassword(Guid id, TemporaryPasswordRequest req)
    {
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User");
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, req.Password ?? "");
        if (!result.Succeeded) throw new AppException(string.Join(" ", result.Errors.Select(e => e.Description)));
        user.MustChangePassword = req.MustChangePassword;
        await users.UpdateAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await security.LogAsync("TemporaryPasswordSet", true, user.Id, user.Email, $"By {current.UserName}{(req.MustChangePassword ? "; change required at next sign-in" : "")}");
        await WriteAudit("TemporaryPasswordSet", user, $"Set a {(req.MustChangePassword ? "temporary " : "")}password for {user.Email}; all of their sessions ended{(req.MustChangePassword ? " and a new password is required at next sign-in" : "")}");
        return NoContent();
    }

    /// <summary>For a lost phone: removes the user's authenticator, recovery codes and trusted browsers and ends their sessions. If two-step verification is required for them, the next sign-in sets it up again.</summary>
    [HttpPost("{id:guid}/reset-mfa")]
    public async Task<IActionResult> ResetMfa(Guid id, ResetMfaRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length < 5) throw new AppException("Give the reason (at least 5 characters), for example how the person's identity was confirmed.");
        if (id == current.UserId) throw new AppException("Use your own Security page to change your two-step verification.");
        var user = await users.FindByIdAsync(id.ToString()) ?? throw new NotFoundException("User");
        if (user.MfaSecret is null) throw new ConflictException("Two-step verification is not on for this user.");
        await mfa.DisableAsync(user, current.UserName, req.Reason.Trim());
        return NoContent();
    }

    private static string ScopeText(Guid? site, Guid? dept) =>
        site is null ? " (all sites)" : dept is null ? $" (site {site})" : $" (site {site}, department {dept})";

    private async Task WriteAudit(string action, ApplicationUser user, string description)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAt = clock.UtcNow, UserId = current.UserId, UserName = current.UserName, Action = action,
            EntityType = "User", EntityId = user.Id, Description = description, CorrelationId = current.CorrelationId
        });
        await db.SaveChangesAsync();
    }
}
