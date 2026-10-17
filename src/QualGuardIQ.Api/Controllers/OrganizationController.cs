using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController]
[Route("api/organization")]
public sealed class OrganizationController(AppDbContext db) : ControllerBase
{
    public sealed record BrandingDto(string ApplicationName, string OrganizationName, string? PrimaryColor);
    public sealed record SettingsDto(string ApplicationName, string OrganizationName, string? PrimaryColor, string TimeZone, bool LoginHistoryEnabled, uint RowVersion);
    public sealed record SiteDto(Guid Id, string Code, string Name, string? Address, string TimeZone, bool IsArchived, IReadOnlyList<DepartmentDto> Departments);
    public sealed record DepartmentDto(Guid Id, string Code, string Name, bool IsArchived);
    public sealed record SaveSiteRequest(string Code, string Name, string? Address, string? TimeZone = null);
    public sealed record SaveDepartmentRequest(string Code, string Name);

    public sealed class SettingsValidator : AbstractValidator<SettingsDto>
    {
        public SettingsValidator()
        {
            RuleFor(x => x.ApplicationName).NotEmpty().MaximumLength(100);
            RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.PrimaryColor).Matches("^#[0-9A-Fa-f]{6}$").When(x => x.PrimaryColor is not null);
            RuleFor(x => x.TimeZone).Must(tz => TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithMessage("Unknown time zone.");
        }
    }

    public sealed class SiteValidator : AbstractValidator<SaveSiteRequest>
    {
        public SiteValidator()
        {
            RuleFor(x => x.Code).NotEmpty().MaximumLength(20).Matches("^[A-Za-z0-9_-]+$");
            RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Address).MaximumLength(500);
            RuleFor(x => x.TimeZone).Must(tz => tz is null || TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _)).WithMessage("Unknown time zone.");
        }
    }

    /// <summary>Public branding for the sign-in page.</summary>
    [HttpGet("branding")]
    [AllowAnonymous]
    public async Task<BrandingDto> Branding(CancellationToken ct)
    {
        var s = await db.OrganizationSettings.AsNoTracking().FirstAsync(ct);
        return new BrandingDto(s.ApplicationName, s.OrganizationName, s.PrimaryColor);
    }

    [HttpGet("settings")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<SettingsDto> GetSettings(CancellationToken ct)
    {
        var s = await db.OrganizationSettings.AsNoTracking().FirstAsync(ct);
        return new SettingsDto(s.ApplicationName, s.OrganizationName, s.PrimaryColor, s.TimeZone, s.LoginHistoryEnabled, s.RowVersion);
    }

    [HttpPut("settings")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> SaveSettings(SettingsDto req, [FromServices] IValidator<SettingsDto> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(req, ct);
        var s = await db.OrganizationSettings.FirstAsync(ct);
        db.Entry(s).Property(x => x.RowVersion).OriginalValue = req.RowVersion;
        s.ApplicationName = req.ApplicationName;
        s.OrganizationName = req.OrganizationName;
        s.PrimaryColor = req.PrimaryColor;
        s.TimeZone = req.TimeZone;
        s.LoginHistoryEnabled = req.LoginHistoryEnabled;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sites are visible to any signed-in user so records can be filed; changing them needs organization admin.</summary>
    [HttpGet("sites")]
    [Authorize]
    public async Task<IReadOnlyList<SiteDto>> Sites(CancellationToken ct) =>
        (await db.Sites.Include(s => s.Departments).AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct))
        .Select(s => new SiteDto(s.Id, s.Code, s.Name, s.Address, s.TimeZone, s.IsArchived,
            s.Departments.OrderBy(d => d.Code).Select(d => new DepartmentDto(d.Id, d.Code, d.Name, d.IsArchived)).ToList())).ToList();

    [HttpPost("sites")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<ActionResult<Guid>> CreateSite(SaveSiteRequest req, [FromServices] IValidator<SaveSiteRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(req, ct);
        if (await db.Sites.AnyAsync(s => s.Code == req.Code, ct)) throw new ConflictException("A site with this code already exists.");
        var site = new Site { Code = req.Code, Name = req.Name, Address = req.Address, TimeZone = req.TimeZone ?? await OrgZoneAsync(ct) };
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return site.Id;
    }

    private async Task<string> OrgZoneAsync(CancellationToken ct) =>
        await db.OrganizationSettings.Select(o => o.TimeZone).FirstOrDefaultAsync(ct) ?? "America/New_York";

    [HttpPut("sites/{siteId:guid}")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> UpdateSite(Guid siteId, SaveSiteRequest req, [FromServices] IValidator<SaveSiteRequest> v, CancellationToken ct)
    {
        await v.ValidateAndThrowAsync(req, ct);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct) ?? throw new NotFoundException("Site");
        if (await db.Sites.AnyAsync(s => s.Code == req.Code && s.Id != siteId, ct)) throw new ConflictException("A site with this code already exists.");
        site.Code = req.Code; site.Name = req.Name; site.Address = req.Address;
        if (req.TimeZone is not null) site.TimeZone = req.TimeZone;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("sites/{siteId:guid}/departments")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<ActionResult<Guid>> CreateDepartment(Guid siteId, SaveDepartmentRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code) || req.Code.Length > 20 || string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 200)
            throw new AppException("Code (up to 20 characters) and name (up to 200) are required.");
        if (!await db.Sites.AnyAsync(s => s.Id == siteId, ct)) throw new NotFoundException("Site");
        if (await db.Departments.AnyAsync(d => d.SiteId == siteId && d.Code == req.Code, ct)) throw new ConflictException("That department code already exists at this site.");
        var d = new Department { SiteId = siteId, Code = req.Code, Name = req.Name };
        db.Departments.Add(d);
        await db.SaveChangesAsync(ct);
        return d.Id;
    }

    [HttpPost("sites/{siteId:guid}/archive")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> ArchiveSite(Guid siteId, CancellationToken ct)
    {
        var s = await db.Sites.FindAsync([siteId], ct) ?? throw new NotFoundException("Site");
        s.IsArchived = true;
        s.ArchivedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
