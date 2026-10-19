using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Api.Controllers;

/// <summary>
/// Organization settings that aren't covered elsewhere: logo, date format, notification e-mails, numbering formats, and a read-only
/// summary of storage, upload scanning and sign-in rules (which are set in the installation's configuration).
/// </summary>
[ApiController]
[Authorize]
[Route("api/settings")]
public sealed partial class SettingsController(AppDbContext db, ICurrentUser current, IClock clock, RecordTypeLabels labels) : ControllerBase
{
    public const int MaxLogoBytes = 512 * 1024;

    /// <summary>The organization logo for the sign-in page, top bar and printouts (404 when none is set).</summary>
    [HttpGet("logo")]
    [AllowAnonymous]
    public async Task<IActionResult> Logo(CancellationToken ct)
    {
        var s = await db.OrganizationSettings.AsNoTracking().Select(x => new { x.LogoImage, x.LogoContentType }).FirstAsync(ct);
        if (s.LogoImage is null || s.LogoContentType is null) return NotFound();
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox";
        return File(s.LogoImage, s.LogoContentType);
    }

    [HttpPost("logo")]
    [RequirePermission(Permissions.AdminOrganization)]
    [RequestSizeLimit(MaxLogoBytes + 64 * 1024)]
    public async Task<IActionResult> UploadLogo(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new AppException("Choose a PNG or JPEG image.");
        if (file.Length > MaxLogoBytes) throw new AppException("The logo is limited to 512 KB.");
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var b = ms.ToArray();
        // the content decides the type, not the file name
        string? type = b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? "image/png"
            : b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg" : null;
        if (type is null) throw new AppException("The logo must be a PNG or JPEG image.");
        var s = await db.OrganizationSettings.FirstAsync(ct);
        s.LogoImage = b;
        s.LogoContentType = type;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("logo")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IActionResult> RemoveLogo(CancellationToken ct)
    {
        var s = await db.OrganizationSettings.FirstAsync(ct);
        s.LogoImage = null;
        s.LogoContentType = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record NumberingDto(string RecordType, string Label, string Prefix, int Padding, bool ResetYearly, long NextValue, string Example);
    public sealed record SaveNumberingRequest(string Prefix, int Padding, bool ResetYearly);

    static string Example(string prefix, int padding, bool yearly, long next, int year) => $"{prefix}{(yearly ? $"{year}-" : "")}{next.ToString().PadLeft(padding, '0')}";

    /// <summary>Organization-wide numbering formats for each record type (sites can't override them from here).</summary>
    [HttpGet("numbering")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<IEnumerable<NumberingDto>> Numbering(CancellationToken ct)
    {
        var year = clock.UtcNow.Year;
        var rows = await db.NumberSequences.AsNoTracking().Where(s => s.SiteId == null).ToListAsync(ct);
        return rows.Select(s => new NumberingDto(s.RecordType, labels.For(s.RecordType), s.Prefix, s.Padding, s.ResetYearly, s.ResetYearly && s.Year != year ? 1 : s.NextValue,
                Example(s.Prefix, s.Padding, s.ResetYearly, s.ResetYearly && s.Year != year ? 1 : s.NextValue, year)))
            .OrderBy(x => x.Label);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._/-]{0,19}$")]
    private static partial Regex PrefixPattern();

    /// <summary>Changes the prefix, digit count or yearly restart of a numbering format. Numbers already given out don't change.</summary>
    [HttpPut("numbering/{recordType}")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<NumberingDto> SaveNumbering(string recordType, SaveNumberingRequest req, CancellationToken ct)
    {
        var prefix = (req.Prefix ?? "").Trim();
        if (!PrefixPattern().IsMatch(prefix)) throw new AppException("The prefix must be 1 to 20 letters, digits, dots, dashes, slashes or underscores, starting with a letter or digit.");
        if (req.Padding is < 1 or > 10) throw new AppException("The number of digits must be 1 to 10.");
        var s = await db.NumberSequences.FirstOrDefaultAsync(x => x.RecordType == recordType && x.SiteId == null, ct) ?? throw new NotFoundException("Numbering format");
        var year = clock.UtcNow.Year;
        var before = Example(s.Prefix, s.Padding, s.ResetYearly, s.NextValue, year);
        if (req.ResetYearly && !s.ResetYearly) s.Year = year;
        s.Prefix = prefix; s.Padding = req.Padding; s.ResetYearly = req.ResetYearly;
        var after = Example(s.Prefix, s.Padding, s.ResetYearly, s.NextValue, year);
        AuditLog.Add(db, current, clock.UtcNow, "Updated", "NumberSequence", s.Id, null,
            $"Changed the numbering format for {labels.For(s.RecordType)}: the next number changes from {before} to {after}");
        await db.SaveChangesAsync(ct);
        return new NumberingDto(s.RecordType, labels.For(s.RecordType), s.Prefix, s.Padding, s.ResetYearly, s.NextValue, after);
    }

    /// <summary>Read-only facts about how this installation is set up (changed in its configuration file, not on screen).</summary>
    [HttpGet("system")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<object> SystemStatus([FromServices] IConfiguration config, [FromServices] IOptions<AuthOptions> auth, [FromServices] QualGuardIQ.Infrastructure.Storage.IMalwareScanner scanner, CancellationToken ct)
    {
        var o = auth.Value;
        var storage = string.Equals(config["Storage:Provider"], "AzureBlob", StringComparison.OrdinalIgnoreCase) ? "Azure Blob Storage" : "S3-compatible storage";
        var endpoint = config["Storage:ServiceUrl"] is { Length: > 0 } url && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : null;
        var scan = await UploadScanningController.StatusAsync(config, scanner, ct);
        var policy = await db.SignInPolicies.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SignInPolicy();
        return new
        {
            storage = new { provider = storage, endpoint, bucket = config["Storage:Bucket"] },
            scanning = scan,
            signIn = new
            {
                policy.MfaRequiredForStaff, policy.MfaRequiredForPortal, policy.TrustedDevicesEnabled, policy.TrustedDeviceDays,
                sessionAbsoluteHours = o.SessionAbsoluteHours, idleTimeoutHours = 8, passwordMinLength = Math.Max(8, o.Password.MinLength),
                lockoutAttempts = Math.Max(3, o.Lockout.MaxFailedAttempts), lockoutMinutes = Math.Max(1, o.Lockout.Minutes),
            },
            email = new { configured = !string.IsNullOrWhiteSpace(config["Email:Host"]) || !string.IsNullOrWhiteSpace(config["Email:PickupDirectory"]), from = config["Email:From"] },
        };
    }
}

/// <summary>
/// A person's own settings: e-mail copies of notifications and date format. Theme is the mode of the person's own Appearance setting
/// (the one place light, dark or system is kept), read and written here so My settings and Appearance always agree.
/// </summary>
[ApiController]
[Authorize]
[Route("api/account/preferences")]
public sealed class PreferencesController(AppDbContext db, ICurrentUser current) : ControllerBase
{
    public sealed record PreferencesDto(bool EmailNotifications, string? DateFormat, string? Theme, string OrganizationDateFormat, bool OrganizationEmailEnabled, string EffectiveDateFormat);
    public sealed record SavePreferencesRequest(bool EmailNotifications, string? DateFormat, string? Theme);

    [HttpGet]
    public async Task<PreferencesDto> Get(CancellationToken ct)
    {
        var uid = current.UserId ?? throw new ForbiddenException();
        var org = await db.OrganizationSettings.AsNoTracking().Select(s => new { s.DateFormat, s.EmailNotificationsEnabled }).FirstAsync(ct);
        var p = await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid, ct);
        var mode = await db.AppearanceSettings.AsNoTracking().Where(x => x.UserId == uid).Select(x => x.Mode).FirstOrDefaultAsync(ct);
        return new PreferencesDto(p?.EmailNotifications ?? true, p?.DateFormat, mode, org.DateFormat, org.EmailNotificationsEnabled, p?.DateFormat ?? org.DateFormat);
    }

    [HttpPut]
    public async Task<PreferencesDto> Save(SavePreferencesRequest req, CancellationToken ct)
    {
        var uid = current.UserId ?? throw new ForbiddenException();
        var fmt = string.IsNullOrWhiteSpace(req.DateFormat) ? null : req.DateFormat.Trim();
        if (fmt is not null && !DateFormats.All.Contains(fmt)) throw new AppException("Unknown date format.");
        var theme = string.IsNullOrWhiteSpace(req.Theme) ? null : req.Theme.Trim().ToLowerInvariant();
        if (theme is not null and not ("light" or "dark" or "system")) throw new AppException("Theme must be light, dark or system.");
        var p = await db.UserPreferences.FirstOrDefaultAsync(x => x.UserId == uid, ct);
        if (p is null) { p = new UserPreference { UserId = uid }; db.UserPreferences.Add(p); }
        p.EmailNotifications = req.EmailNotifications;
        p.DateFormat = fmt;
        var look = await db.AppearanceSettings.FirstOrDefaultAsync(x => x.UserId == uid, ct);
        if (look is null && theme is not null) { look = new AppearanceSetting { UserId = uid }; db.AppearanceSettings.Add(look); }
        if (look is not null && look.Mode != theme) look.Mode = theme;
        await db.SaveChangesAsync(ct);
        return await Get(ct);
    }
}
