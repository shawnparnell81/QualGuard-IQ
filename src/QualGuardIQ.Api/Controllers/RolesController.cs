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
    public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, uint RowVersion);
    public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions, uint? RowVersion);

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
        .Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions.Select(p => p.Permission).Order().ToList(), r.RowVersion)).ToList();

    [HttpPost]
    public async Task<ActionResult<Guid>> Create(SaveRoleRequest req, [FromServices] IValidator<SaveRoleRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(req, ct);
        if (await db.QmsRoles.AnyAsync(r => r.Name == req.Name, ct)) throw new ConflictException("A role with this name already exists.");
        var role = new Role { Name = req.Name, Description = req.Description };
        role.Permissions.AddRange(req.Permissions.Distinct().Select(p => new RolePermission { RoleId = role.Id, Permission = p }));
        db.QmsRoles.Add(role);
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
        role.Permissions.RemoveAll(p => !req.Permissions.Contains(p.Permission));
        foreach (var p in req.Permissions.Distinct().Where(p => role.Permissions.All(x => x.Permission != p)))
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = p });
        var after = string.Join(", ", role.Permissions.Select(p => p.Permission).Order());
        if (before != after) role.Description = req.Description; // permission list changes are audited below
        db.Entry(role).Property(r => r.UpdatedAt).IsModified = true;
        await db.SaveChangesAsync(ct);
        if (before != after)
        {
            db.AuditEntries.Add(new Domain.Audit.AuditEntry
            {
                OccurredAt = DateTimeOffset.UtcNow, UserId = HttpContext.RequestServices.GetRequiredService<ICurrentUser>().UserId,
                UserName = HttpContext.RequestServices.GetRequiredService<ICurrentUser>().UserName, Action = "PermissionsChanged",
                EntityType = nameof(Role), EntityId = role.Id, Description = $"Changed permissions of role {role.Name}",
                Changes = System.Text.Json.JsonSerializer.Serialize(new { from = before, to = after })
            });
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }
}
