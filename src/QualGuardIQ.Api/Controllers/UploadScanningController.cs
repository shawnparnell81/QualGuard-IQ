using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Api.Infrastructure;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Whether uploaded files are checked for viruses on this installation, for the notice on the admin pages.</summary>
[ApiController]
[Route("api/admin/upload-scanning")]
[RequirePermission(Permissions.AdminOrganization)]
public sealed class UploadScanningController(IConfiguration config, IMalwareScanner scanner) : ControllerBase
{
    /// <param name="Enabled">Scanning is turned on in the settings.</param>
    /// <param name="Reachable">Whether the scanner answered just now; null when scanning is off.</param>
    /// <param name="Message">Plain-language problem to show, or null when all is well.</param>
    public sealed record ScanningStatus(bool Enabled, string Provider, bool? Reachable, bool AllowWhenUnavailable, string? Message);

    [HttpGet]
    public async Task<ScanningStatus> Get(CancellationToken ct)
    {
        var provider = config["MalwareScanning:Provider"] is { Length: > 0 } p ? p : "None";
        var enabled = string.Equals(provider, "ClamAV", StringComparison.OrdinalIgnoreCase);
        bool? reachable = enabled && scanner is IMalwareScannerProbe probe ? await probe.IsReachableAsync(ct) : null;
        return new ScanningStatus(enabled, provider, reachable, config.GetValue("MalwareScanning:AllowWhenUnavailable", false),
            reachable == false ? MalwareScannerHealthCheck.UnavailableMessage(config) : null);
    }
}
