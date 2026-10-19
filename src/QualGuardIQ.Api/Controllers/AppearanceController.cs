using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Appearance;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

/// <summary>
/// Appearance: the organization default (mode, primary and accent colours, palette of each form family), changed by holders of the
/// organization settings permission and written to the audit trail, and each person's own override.
/// </summary>
[ApiController, Authorize, Route("api/appearance"), Tags("Appearance")]
public sealed class AppearanceController(AppDbContext db, ICurrentUser current, IAccessService access) : ControllerBase
{
    public sealed record Defaults(string Mode, string PrimaryColor, string AccentColor, Dictionary<string, FamilyPalette> FormPalettes, Dictionary<string, PaletteColours> Palettes);
    public sealed record PaletteColours(string Edge, string Accent);
    public sealed record AppearanceDto(AppearanceValues? Organization, AppearanceValues? Personal, bool CanEditOrganization, Defaults Defaults);

    static Defaults Builtin() => new("system", AppearanceModel.Blue, AppearanceModel.Green, AppearanceModel.DefaultFormPalettes(),
        new() { ["palette1"] = new(AppearanceModel.Green, AppearanceModel.Blue), ["palette2"] = new(AppearanceModel.Slate, AppearanceModel.Blue) });

    /// <summary>The organization default, the signed-in person's override and the built-in look.</summary>
    [HttpGet]
    public async Task<AppearanceDto> Get(CancellationToken ct)
    {
        var uid = current.UserId!.Value;
        var rows = await db.AppearanceSettings.AsNoTracking().Where(x => x.UserId == null || x.UserId == uid).ToListAsync(ct);
        return new AppearanceDto(AppearanceModel.Read(rows.FirstOrDefault(x => x.UserId == null)), AppearanceModel.Read(rows.FirstOrDefault(x => x.UserId == uid)),
            await access.HasPermissionAsync(uid, Permissions.AdminOrganization, ct), Builtin());
    }

    /// <summary>Sets the organization default; send nulls (or an empty palette map) to go back to the built-in look.</summary>
    [HttpPut("organization")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> SaveOrganization(AppearanceValues v, CancellationToken ct) { await SaveAsync(null, v, ct); return NoContent(); }

    /// <summary>Sets the signed-in person's own override; nulls inherit the organization default.</summary>
    [HttpPut("me")]
    public async Task<IActionResult> SaveMine(AppearanceValues v, CancellationToken ct) { await SaveAsync(current.UserId!.Value, v, ct); return NoContent(); }

    async Task SaveAsync(Guid? userId, AppearanceValues v, CancellationToken ct)
    {
        AppearanceModel.Validate(v);
        var row = await db.AppearanceSettings.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) { row = new AppearanceSetting { UserId = userId }; db.AppearanceSettings.Add(row); }
        AppearanceModel.Write(row, v);
        await db.SaveChangesAsync(ct);
    }
}
