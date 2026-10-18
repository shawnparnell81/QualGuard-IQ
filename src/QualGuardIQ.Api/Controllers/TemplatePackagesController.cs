using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QualGuardIQ.Infrastructure.Modules.Templates;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Template marketplace: industry packs and library templates, install as draft form templates, package export and import.</summary>
[ApiController, Authorize, Route("api/v1/template-packages"), Tags("Template Packages")]
public sealed class TemplatePackagesController(TemplatePackageService packages) : ControllerBase
{
    /// <summary>Built-in industry packs and imported or exported packages, with how many of their templates are installed.</summary>
    [HttpGet] public Task<IReadOnlyList<PackageSummary>> List(CancellationToken ct) => packages.ListAsync(ct);

    /// <summary>Library categories with their template counts.</summary>
    [HttpGet("library/categories")] public Task<IReadOnlyList<LibraryCategoryDto>> Categories(CancellationToken ct) => packages.CategoriesAsync(ct);

    /// <summary>Browse and search the library templates.</summary>
    [HttpGet("library/templates")]
    public Task<IReadOnlyList<PackageTemplate>> LibraryTemplates([FromQuery] string? category, [FromQuery] string? q, [FromQuery] string? pack, CancellationToken ct) =>
        packages.LibraryTemplatesAsync(category, q, pack, ct);

    /// <summary>A library template's full definition, for the side-by-side preview before installing.</summary>
    [HttpGet("library/templates/{key}")] public Task<TemplatePreview> Preview(string key, CancellationToken ct) => packages.PreviewAsync(key, ct);

    /// <summary>A package and its templates.</summary>
    [HttpGet("{key}")] public Task<PackageDetail> Get(string key, [FromQuery] string? version, CancellationToken ct) => packages.GetAsync(key, version, ct);

    /// <summary>Install a package, or selected templates from it (key "library" installs individual library templates), as draft form templates.</summary>
    [EnableRateLimiting("processing")]
    [HttpPost("{key}/install")] public Task<InstallResult> Install(string key, InstallRequest req, CancellationToken ct) => packages.InstallAsync(key, req, ct);

    /// <summary>Export form templates as a package file (.qgpkg).</summary>
    [EnableRateLimiting("processing")]
    [HttpPost("export")]
    public async Task<IActionResult> Export(ExportRequest req, CancellationToken ct)
    {
        var f = await packages.ExportAsync(req, ct);
        return File(f.Content, "application/zip", f.FileName);
    }

    /// <summary>Import a package file exported by another installation. Every definition is hash-checked and validated; nothing is installed until requested.</summary>
    [EnableRateLimiting("processing")]
    [HttpPost("import")]
    [RequestSizeLimit(TemplatePackageService.MaxPackageBytes + 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = TemplatePackageService.MaxPackageBytes + 1024 * 1024)]
    public async Task<PackageSummary> Import(IFormFile file, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        return await packages.ImportAsync(s, file.FileName, ct);
    }
}
