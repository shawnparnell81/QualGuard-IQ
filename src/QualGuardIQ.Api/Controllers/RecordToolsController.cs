using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Records;
using QualGuardIQ.Infrastructure.Services.Export;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Print, PDF, export and delete for any record type (the toolbar every record page shows).</summary>
[ApiController]
[Authorize]
[Route("api/records")]
public sealed class RecordToolsController(IRecordRegistry records, RecordDeletionService deletion, RecordExportService export, RecordTypeLabels labels, AppDbContext db) : ControllerBase
{
    public sealed record ReasonRequest(string? Reason);
    public sealed record CapabilitiesDto(string Type, string TypeLabel, Guid Id, string Number, string Title, string? Status, string Url, DeleteCheck Delete, bool ImportedReadOnly = false);

    /// <summary>What the current user can do with the record from the shared toolbar (export is open to anyone who can view it).</summary>
    [HttpGet("{type}/{id:guid}/capabilities")]
    public async Task<CapabilitiesDto> Capabilities(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        return new CapabilitiesDto(r.Type, labels.For(r.Type), r.Id, r.Number, r.Title, r.Status, r.Url, await deletion.CheckAsync(r.Type, r.Id, ct),
            await ImportedRecords.IsReadOnlyAsync(db, r.Id, ct));
    }

    /// <summary>
    /// Finds the record type of an id from a page address, so record pages get the toolbar without each page wiring it up.
    /// Returns 404 when the id isn't a record the user may view.
    /// </summary>
    [HttpGet("locate/{id:guid}")]
    public async Task<CapabilitiesDto> Locate(Guid id, [FromQuery] string? path, CancellationToken ct)
    {
        var indexed = await db.SearchEntries.AsNoTracking().Where(e => e.RecordId == id).Select(e => e.RecordType).ToListAsync(ct);
        RecordInfo? first = null;
        foreach (var t in indexed.Concat(records.KnownTypes.Except(indexed, StringComparer.OrdinalIgnoreCase)))
        {
            RecordInfo r;
            try { r = await records.GetAsync(t, id, ct); } catch (NotFoundException) { continue; }
            // a document and its revisions share a page; prefer the type whose own page is being shown
            if (path is not { Length: > 0 } || r.Url.Split('?')[0].Equals(path, StringComparison.OrdinalIgnoreCase)) return await Capabilities(r.Type, id, ct);
            first ??= r;
        }
        if (first is not null) return await Capabilities(first.Type, id, ct);
        throw new NotFoundException("Record");
    }

    [HttpPost("{type}/{id:guid}/delete")]
    public Task<DeletedRecordDto> Delete(string type, Guid id, ReasonRequest req, CancellationToken ct) => deletion.DeleteAsync(type, id, req.Reason, ct);

    /// <summary>The record as PDF, CSV, Excel (.xlsx) or Word (.docx). Form records and Sequfloh documents print in their own layout.</summary>
    [EnableRateLimiting("processing")]
    [HttpGet("{type}/{id:guid}/export")]
    public async Task<IActionResult> Export(string type, Guid id, [FromQuery] string format, [FromQuery] bool inline, CancellationToken ct)
    {
        var f = await export.RecordAsync(type, id, format, ct);
        Response.Headers.CacheControl = "private, no-store";
        if (inline && f.ContentType == "application/pdf")
        {
            Response.Headers.ContentDisposition = $"inline; filename=\"{f.FileName}\"";
            return File(f.Content, f.ContentType);
        }
        return File(f.Content, f.ContentType, f.FileName);
    }
}

/// <summary>Deleted records: list them and bring them back. Visible to people holding the module's delete permission.</summary>
[ApiController]
[Authorize]
[Route("api/deleted-records")]
public sealed class DeletedRecordsController(RecordDeletionService deletion) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DeletedRecordDto>> List([FromQuery] string? type, [FromQuery] string? q, [FromQuery] bool includeRestored, CancellationToken ct) =>
        deletion.ListAsync(type, q, includeRestored, ct);

    [HttpPost("{id:guid}/restore")]
    public Task<DeletedRecordDto> Restore(Guid id, RecordToolsController.ReasonRequest req, CancellationToken ct) => deletion.RestoreAsync(id, req.Reason, ct);
}

/// <summary>Exports of lists as the person sees them (visible columns, current filters and sort).</summary>
[ApiController]
[Authorize]
[Route("api/export")]
public sealed class ExportController(RecordExportService export) : ControllerBase
{
    [EnableRateLimiting("processing")]
    [HttpPost("table")]
    [RequestSizeLimit(30L * 1024 * 1024)]
    public async Task<IActionResult> Table(TableExportRequest req, CancellationToken ct)
    {
        var f = await export.TableAsync(req, ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(f.Content, f.ContentType, f.FileName);
    }
}
