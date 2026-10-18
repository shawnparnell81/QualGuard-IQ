using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Infrastructure.Modules.Documents;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Enterprise Template Library: catalog views, list functions and template actions above the form designer.</summary>
[ApiController]
[Authorize]
[Route("api/template-library"), Route("api/v1/template-library")]
public sealed class TemplateLibraryController(TemplateCatalogService catalog) : ControllerBase
{
    public sealed record ReasonRequest(string Reason);
    public sealed record FavoriteRequest(bool Favorite);

    [HttpGet]
    public Task<CatalogPage> List([FromQuery] string? view, [FromQuery] string? q, [FromQuery] Guid? categoryId, [FromQuery] string? module, [FromQuery] string? industryPack,
        [FromQuery] Guid? siteId, [FromQuery] Guid? departmentId, [FromQuery] Guid? ownerId, [FromQuery] DocumentStatus? status, [FromQuery] TemplateSource? source,
        [FromQuery] bool favorites = false, [FromQuery] string? sort = null, [FromQuery] bool desc = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => catalog.ListAsync(new CatalogQuery(view, q, categoryId, module, industryPack, siteId, departmentId, ownerId, status, source, favorites, sort, desc, page, pageSize), ct);

    [HttpGet("categories")] public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => catalog.CategoriesAsync(ct);
    [HttpPost("categories")] public Task<Guid> CreateCategory(CreateCategoryRequest req, CancellationToken ct) => catalog.CreateCategoryAsync(req, ct);
    [HttpPut("{id:guid}/classification")] public async Task<IActionResult> Classify(Guid id, ClassifyRequest req, CancellationToken ct) { await catalog.ClassifyAsync(id, req, ct); return NoContent(); }
    [HttpPut("{id:guid}/favorite")] public async Task<IActionResult> Favorite(Guid id, FavoriteRequest req, CancellationToken ct) { await catalog.SetFavoriteAsync(id, req.Favorite, ct); return NoContent(); }
    [HttpPost("{id:guid}/clone")] public Task<Guid> Clone(Guid id, QualGuardIQ.Infrastructure.Modules.Documents.CloneRequest req, CancellationToken ct) => catalog.CloneAsync(id, req, ct);
    [HttpPost("{id:guid}/restore")] public async Task<IActionResult> Restore(Guid id, ReasonRequest req, CancellationToken ct) { await catalog.RestoreAsync(id, req.Reason, ct); return NoContent(); }
    [HttpGet("{id:guid}/compare")] public Task<RevisionCompare> Compare(Guid id, [FromQuery] Guid from, [FromQuery] Guid to, CancellationToken ct) => catalog.CompareAsync(id, from, to, ct);
    [HttpGet("{id:guid}/usage")] public Task<UsageDto> Usage(Guid id, CancellationToken ct) => catalog.UsageAsync(id, ct);
    [HttpGet("{id:guid}/history")] public Task<IReadOnlyList<HistoryDto>> History(Guid id, CancellationToken ct) => catalog.HistoryAsync(id, ct);
    [HttpGet("views")] public Task<IReadOnlyList<SavedViewDto>> Views(CancellationToken ct) => catalog.ViewsAsync(ct);
    [HttpPost("views")] public Task<Guid> SaveView(SaveViewRequest req, CancellationToken ct) => catalog.SaveViewAsync(req, ct);
    [HttpDelete("views/{viewId:guid}")] public async Task<IActionResult> DeleteView(Guid viewId, CancellationToken ct) { await catalog.DeleteViewAsync(viewId, ct); return NoContent(); }
}
