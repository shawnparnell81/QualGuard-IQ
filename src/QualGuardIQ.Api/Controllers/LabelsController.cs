using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Logistics;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Label printers, print routing, the print queue, reprints with a reason, previews and PDF fallback downloads.</summary>
[ApiController, Authorize, Route("api/labels")]
public sealed class LabelsController(LabelPrintService svc) : ControllerBase
{
    [HttpGet("printers")] [RequirePermission(Permissions.InventoryView)] public Task<List<PrinterRow>> Printers([FromQuery] Guid? siteId, CancellationToken ct) => svc.PrintersAsync(siteId, ct);
    [HttpPost("printers")] [RequirePermission(Permissions.InventoryApprove)] public async Task<object> CreatePrinter(PrinterInput r, CancellationToken ct) { var p = await svc.SavePrinterAsync(null, r, ct); return new { p.Id, p.Code }; }
    [HttpPut("printers/{id:guid}")] [RequirePermission(Permissions.InventoryApprove)] public async Task<IActionResult> UpdatePrinter(Guid id, PrinterInput r, CancellationToken ct) { await svc.SavePrinterAsync(id, r, ct); return NoContent(); }
    /// <summary>Prints a test label and records the printer status (online, offline, error).</summary>
    [HttpPost("printers/{id:guid}/test")] [RequirePermission(Permissions.InventoryApprove)] public Task<PrintOutcome> TestPrinter(Guid id, CancellationToken ct) => svc.TestPrinterAsync(id, ct);

    [HttpGet("routes")] [RequirePermission(Permissions.InventoryView)] public Task<List<RouteRow>> Routes(CancellationToken ct) => svc.RoutesAsync(ct);
    [HttpPost("routes")] [RequirePermission(Permissions.InventoryApprove)] public async Task<object> CreateRoute(RouteInput r, CancellationToken ct) => new { (await svc.SaveRouteAsync(null, r, ct)).Id };
    [HttpPut("routes/{id:guid}")] [RequirePermission(Permissions.InventoryApprove)] public async Task<IActionResult> UpdateRoute(Guid id, RouteInput r, CancellationToken ct) { await svc.SaveRouteAsync(id, r, ct); return NoContent(); }
    /// <summary>Which route and printer a label would use (the X-Workstation header names the workstation).</summary>
    [HttpGet("routes/resolve")] [RequirePermission(Permissions.InventoryView)]
    public async Task<object> Resolve([FromQuery] LabelType labelType, [FromQuery] Guid siteId, [FromQuery] Guid? locationId, [FromQuery] string? workstation, [FromQuery] Guid? userId, CancellationToken ct)
    { var r = await svc.ResolveAsync(labelType, siteId, locationId, workstation, userId, ct); return new { routeId = r.Route?.Id, printerId = r.Printer?.Id, printer = r.Printer?.Code, r.Explanation }; }

    /// <summary>Prints the label(s) for a record: the given printer, the routed printer, or a PDF download when none applies.</summary>
    [HttpPost("print")] public Task<List<PrintOutcome>> Print(PrintRequest r, CancellationToken ct) => svc.PrintAsync(r, ct);
    [HttpGet("preview")] public async Task<FileContentResult> Preview([FromQuery] LabelType labelType, [FromQuery] string refType, [FromQuery] Guid refId, CancellationToken ct) =>
        File(await svc.PreviewAsync(labelType, refType, refId, ct), "application/pdf", "label-preview.pdf");

    [HttpGet("jobs")] public Task<JobPage> Jobs([FromQuery] Guid? siteId, [FromQuery] PrintJobStatus? status, [FromQuery] Guid? printerId, [FromQuery] string? refType, [FromQuery] Guid? refId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) => svc.JobsAsync(siteId, status, printerId, refType, refId, page, pageSize, ct);
    [HttpPost("jobs/{id:guid}/retry")] public Task<PrintOutcome> Retry(Guid id, CancellationToken ct) => svc.RetryAsync(id, ct);
    /// <summary>Reprints a label exactly as it was first printed; the reason is required and recorded.</summary>
    [HttpPost("jobs/{id:guid}/reprint")] public Task<PrintOutcome> Reprint(Guid id, ReprintInput r, CancellationToken ct) => svc.ReprintAsync(id, r, ct);
    [HttpPost("jobs/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await svc.CancelAsync(id, ct); return NoContent(); }
    [HttpGet("jobs/{id:guid}/pdf")] public async Task<FileContentResult> Pdf(Guid id, CancellationToken ct) => File(await svc.JobPdfAsync(id, ct), "application/pdf", "label.pdf");
    [HttpGet("jobs/{id:guid}/zpl")] public async Task<ContentResult> Zpl(Guid id, CancellationToken ct) => Content(await svc.JobZplAsync(id, ct), "text/plain");
}

/// <summary>Runs automatic label printing after a successful transaction and reports the result in the X-Label-Print header.</summary>
public static class LabelHook
{
    public static async Task AutoAsync(this ControllerBase c, LabelPrintService labels, LabelType type, string refType, Guid refId, CancellationToken ct)
    {
        var res = await labels.AutoAsync(type, refType, refId, ct);
        if (res.Count == 0) return;
        c.Response.Headers["X-Label-Print"] = $"printed={res.Count(x => x.Status == PrintJobStatus.Printed)};failed={res.Count(x => x.Status == PrintJobStatus.Failed)};held={res.Count(x => x.Status == PrintJobStatus.Held)};manual={res.Count(x => x.Status == PrintJobStatus.ManualDownload)}";
    }
}
