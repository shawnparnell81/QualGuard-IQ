using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Infrastructure.Modules.MasterLists;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Governed master lists (calibration, documents, drawings, suppliers, training, risks …). Each list reads the owning module's records;
/// bulk import and bulk update go through the Import Center with the "master list" purpose.</summary>
[ApiController]
[Authorize]
[Route("api/master-lists")]
public sealed class MasterListsController(MasterListService lists) : ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<object>> Types(CancellationToken ct) => lists.TypesAsync(ct);

    [HttpGet("{type}")]
    public Task<MasterListPage> List(string type, [FromQuery] string? q, [FromQuery] bool includeInactive = false, [FromQuery] string? status = null, [FromQuery] string? sort = null,
        [FromQuery] bool desc = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => lists.ListAsync(type, q, includeInactive, status, sort, desc, page, pageSize, ct);

    [HttpGet("{type}/export")]
    public async Task<IActionResult> Export(string type, [FromQuery] string format = "xlsx", [FromQuery] string? q = null, [FromQuery] bool includeInactive = false, [FromQuery] string? status = null,
        [FromQuery] string? sort = null, [FromQuery] bool desc = false, CancellationToken ct = default)
    {
        var (content, contentType, name) = await lists.ExportAsync(type, format == "csv" ? "csv" : "xlsx", q, includeInactive, status, sort, desc, ct);
        return File(content, contentType, name);
    }

    [HttpGet("{type}/history")] public Task<IReadOnlyList<object>> History(string type, [FromQuery] int take = 100, CancellationToken ct = default) => lists.HistoryAsync(type, take, ct);
    [HttpGet("{type}/views")] public Task<IReadOnlyList<object>> Views(string type, CancellationToken ct) => lists.ViewsAsync(type, ct);
    [HttpPost("{type}/views")] public Task<Guid> SaveView(string type, SaveMasterViewRequest req, CancellationToken ct) => lists.SaveViewAsync(type, req, ct);
    [HttpDelete("{type}/views/{id:guid}")] public async Task<IActionResult> DeleteView(string type, Guid id, CancellationToken ct) { await lists.DeleteViewAsync(type, id, ct); return NoContent(); }
}
