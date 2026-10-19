using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController]
[Route("api/admin/roles")]
[RequirePermission(Permissions.AdminRoles)]
public sealed class RolesController(AppDbContext db) : ControllerBase
{
    public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, uint RowVersion, bool RequiresMfa = false);
    public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions, uint? RowVersion, bool? RequiresMfa = null);
    public sealed record RoleMfaRequest(bool RequiresMfa);

    public sealed class SaveRoleValidator : AbstractValidator<SaveRoleRequest>
    {
        public SaveRoleValidator()
        {
            RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Description).MaximumLength(500);
            RuleForEach(x => x.Permissions).Must(p => Domain.Security.Permissions.All.Contains(p)).WithMessage("Unknown permission '{PropertyValue}'.");
        }
    }

    [HttpGet("permissions")]
    public IReadOnlyList<string> Catalog() => Permissions.All;

    [HttpGet]
    public async Task<IReadOnlyList<RoleDto>> List(CancellationToken ct) =>
        (await db.QmsRoles.Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(ct))
        .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions.Select(p => p.Permission).Order().ToList(), r.RowVersion, r.RequiresMfa)).ToList();

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(SaveRoleRequest req, [FromServices] IValidator<SaveRoleRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(req, ct);
        if (await db.QmsRoles.AnyAsync(r => r.Name == req.Name, ct)) throw new ConflictException("A role with this name already exists.");
        var role = new Role { Name = req.Name, Description = req.Description, RequiresMfa = req.RequiresMfa ?? false };
        role.Permissions.AddRange(req.Permissions.Distinct().Select(p => new RolePermission { RoleId = role.Id, Permission = p }));
        db.QmsRoles.Add(role);
        var granted = role.Permissions.Select(p => p.Permission).Order().ToList();
        // saved together with the role, so the role never exists without a record of what it grants
        AuditLog.Add(db, HttpContext.RequestServices.GetRequiredService<ICurrentUser>(), DateTimeOffset.UtcNow, "PermissionsChanged", nameof(Role), role.Id, null,
            $"Created role {role.Name} with {granted.Count} permission{(granted.Count == 1 ? "" : "s")}: {(granted.Count == 0 ? "none" : string.Join(", ", granted))}", new { from = "", to = string.Join(", ", granted) });
        await db.SaveChangesAsync(ct);
        return role.Id;
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveRoleRequest req, [FromServices] IValidator<SaveRoleRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(req, ct);
        var role = await db.QmsRoles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role");
        if (role.IsSystem) throw new AppException("The built-in administrator role cannot be changed.");
        if (req.RowVersion is { } rv) db.Entry(role).Property(r => r.RowVersion).OriginalValue = rv;
        var before = string.Join(", ", role.Permissions.Select(p => p.Permission).Order());
        role.Name = req.Name;
        role.Description = req.Description;
        if (req.RequiresMfa is { } mfa) role.RequiresMfa = mfa;
        role.Permissions.RemoveAll(p => !req.Permissions.Contains(p.Permission));
        foreach (var p in req.Permissions.Distinct().Where(p => role.Permissions.All(x => x.Permission != p)))
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
        var after = string.Join(", ", role.Permissions.Select(p => p.Permission).Order());
        if (before != after) role.Description = req.Description; // permission list changes are audited below
        db.Entry(role).Property(r => r.UpdatedAt).IsModified = true;
        if (before != after)
        {
            // in the same save as the change itself
            var was = before.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var now = after.Split(", ", StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var added = now.Except(was).Order().ToList();
            var removed = was.Except(now).Order().ToList();
            var parts = new List<string>();
            if (added.Count > 0) parts.Add("added " + string.Join(", ", added));
            if (removed.Count > 0) parts.Add("removed " + string.Join(", ", removed));
            AuditLog.Add(db, HttpContext.RequestServices.GetRequiredService<ICurrentUser>(), DateTimeOffset.UtcNow, "PermissionsChanged", nameof(Role), role.Id, null,
                $"Changed permissions of role {role.Name}: {string.Join("; ", parts)}", new { from = before, to = after });
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Whether people holding this role must use two-step verification. Allowed on every role, including the built-in
    /// administrator role. People already signed in are asked to set it up at their next sign-in.
    /// </summary>
    [HttpPut("{id:guid}/mfa")]
    public async Task<IActionResult> SetMfa(Guid id, RoleMfaRequest req, CancellationToken ct)
    {
        var role = await db.QmsRoles.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Role");
        role.RequiresMfa = req.RequiresMfa;
        await db.SaveChangesAsync(ct); // Role is audited: the change is written to the audit trail with before and after values
        return NoContent();
    }
}
