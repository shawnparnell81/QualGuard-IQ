using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Whether uploaded files are checked for viruses on this installation, for the notice on the admin pages.</summary>
[ApiController]
[Route("api/admin/upload-scanning")]
[RequirePermission(Permissions.AdminOrganization)]
public sealed class UploadScanningController(IConfiguration config) : ControllerBase
{
    public sealed record ScanningStatus(bool Enabled, string Provider);

    [HttpGet]
    public ScanningStatus Get()
    {
        var provider = config["MalwareScanning:Provider"] is { Length: > 0 } p ? p : "None";
        return new ScanningStatus(string.Equals(provider, "ClamAV", StringComparison.OrdinalIgnoreCase), provider);
    }
}
