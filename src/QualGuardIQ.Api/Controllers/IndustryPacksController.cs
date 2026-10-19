using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Infrastructure.Modules.Templates;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Industry pack picker for organization setup and Admin: preview the forms in each pack and install one or more packs.</summary>
[ApiController, Authorize, Route("api/industry-packs"), Tags("Industry Packs")]
public sealed class IndustryPacksController(IndustryPackSetupService packs) : ControllerBase
{
    /// <summary>Whether setup has chosen packs yet, whether the current user may install, and every pack with its forms.</summary>
    [HttpGet] public Task<IndustryPackStatus> Status(CancellationToken ct) => packs.StatusAsync(ct);

    /// <summary>Installs the chosen packs at a site as draft form templates (needs templates.install at that site).</summary>
    [HttpPost("install")] public Task<IndustryPackInstallResult> Install(IndustryPackInstallRequest req, CancellationToken ct) => packs.InstallAsync(req, ct);

    /// <summary>Finishes the setup step without installing a pack.</summary>
    [HttpPost("skip")] public async Task<IActionResult> Skip(CancellationToken ct) { await packs.SkipAsync(ct); return NoContent(); }
}
