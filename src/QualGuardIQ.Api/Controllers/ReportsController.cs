using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Reports;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Report catalog, run and export. Each report additionally checks its source module's view scope.</summary>
[ApiController, Authorize, Route("api/reports")]
public sealed class ReportsController(AppDbContext db, ReportService svc, ICurrentUser current, IClock clock) : ControllerBase
{
    [HttpGet] [RequirePermission(Permissions.ReportsView)]
    public Task<List<ReportDefinition>> List(CancellationToken ct) => svc.AvailableAsync(ct);

    [HttpGet("{key}")] [RequirePermission(Permissions.ReportsView)]
    public async Task<ReportResult> Run(string key, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, [FromQuery] Guid? departmentId, CancellationToken ct)
    {
        await EnsureAvailable(key, ct);
        return await svc.RunAsync(key, new(from, to, siteId, departmentId), ct);
    }

    [HttpGet("{key}/csv")] [RequirePermission(Permissions.ReportsExport)]
    public Task<IActionResult> Csv(string key, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, [FromQuery] Guid? departmentId, CancellationToken ct) =>
        Export(key, "csv", new(from, to, siteId, departmentId), ct);

    /// <summary>Excel workbook of the report (typed cells, frozen header).</summary>
    [HttpGet("{key}/xlsx")] [RequirePermission(Permissions.ReportsExport)]
    public Task<IActionResult> Xlsx(string key, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, [FromQuery] Guid? departmentId, CancellationToken ct) =>
        Export(key, "xlsx", new(from, to, siteId, departmentId), ct);

    /// <summary>PDF of the report table (landscape, paginated, header repeated on each page).</summary>
    [HttpGet("{key}/pdf")] [RequirePermission(Permissions.ReportsExport)]
    public Task<IActionResult> Pdf(string key, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? siteId, [FromQuery] Guid? departmentId, CancellationToken ct) =>
        Export(key, "pdf", new(from, to, siteId, departmentId), ct);

    private async Task<IActionResult> Export(string key, string format, ReportParams p, CancellationToken ct)
    {
        await EnsureAvailable(key, ct);
        var r = await svc.RunAsync(key, p, ct);
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAt = clock.UtcNow, UserId = current.UserId, UserName = current.UserName, Action = "Exported", EntityType = "Report", EntityId = Guid.Empty,
            Description = $"Exported report '{r.Definition.Title}' ({r.Rows.Count} rows, {r.Params.From:yyyy-MM-dd} to {r.Params.To:yyyy-MM-dd}) as {format.ToUpperInvariant()}", CorrelationId = current.CorrelationId
        });
        await db.SaveChangesAsync(ct);
        var name = $"{key}-{r.Params.To:yyyyMMdd}.{format}";
        return format switch
        {
            "xlsx" => File(ReportService.ToXlsx(r, r.Definition.Title), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name),
            "pdf" => File(ReportService.ToPdf(r, r.Definition.Title), "application/pdf", name),
            _ => File(Encoding.UTF8.GetBytes(ReportService.ToCsv(r)), "text/csv", name)
        };
    }

    private async Task EnsureAvailable(string key, CancellationToken ct)
    {
        if (!(await svc.AvailableAsync(ct)).Any(d => d.Key == key)) throw new NotFoundException("Report");
    }
}
