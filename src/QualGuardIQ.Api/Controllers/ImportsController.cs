using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Infrastructure.Modules.Imports;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Import Center: upload, inspect, map, validate (dry run), approve, import, roll back; issues, history, originals and error rows.</summary>
[ApiController]
[Authorize]
[Route("api/imports")]
public sealed class ImportsController(ImportCenterService imports, AppDbContext db) : ControllerBase
{
    public sealed record JobListItem(Guid Id, string Number, string OriginalFileName, string FileType, string ImportMode, string? TargetModule, ImportJobStatus Status, int Progress,
        int TotalRows, int ImportedRows, int WarningCount, int ErrorCount, DateTimeOffset SubmittedAt, string SubmittedBy, string SiteCode);
    public sealed record MappingDto(Guid Id, string SourceLocation, string SourceName, string SourceType, string? TargetField, string? TargetType, string? TransformationRule,
        int MappingConfidence, bool UserConfirmed, string? DefaultValue, bool RequiredValue);
    public sealed record IssueDto(Guid Id, string SourceLocation, int? RowNumber, ImportIssueSeverity Severity, string IssueCode, string Description, string? SuggestedResolution, string? UserResolution, DateTimeOffset? ResolvedAt);
    public sealed record HistoryDto(DateTimeOffset EventDate, string Event, string ActorName, string? PreviousValue, string? NewValue, string? Message);
    public sealed record JobDetail(Guid Id, string Number, Guid SiteId, Guid? DepartmentId, string OriginalFileName, string FileType, long FileSize, string FileHash, string ImportMode,
        string? TargetModule, Guid? TargetTemplateId, ImportJobStatus Status, int Progress, int TotalRows, int ImportedRows, int WarningCount, int ErrorCount, Guid SubmittedById, string SubmittedBy,
        DateTimeOffset SubmittedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, Guid? ApprovedById, DateTimeOffset? ApprovedAt, JsonElement Settings, JsonElement Analysis, JsonElement Summary,
        uint RowVersion, IReadOnlyList<MappingDto> Mappings, IReadOnlyList<IssueDto> Issues, IReadOnlyList<HistoryDto> History, IReadOnlyList<TargetField> TargetFields, bool Background);
    public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int PageNumber, int PageSize);
    public sealed record ReasonRequest(string Reason);
    public sealed record SubmitRequest(bool SkipInvalidRows);

    [HttpGet("targets")] public IReadOnlyList<TargetInfo> Targets() => imports.Targets;

    [HttpGet]
    public async Task<Page<JobListItem>> List([FromQuery] ImportJobStatus? status, [FromQuery] string? q, [FromQuery] bool warnings = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await imports.ScopedAsync(ct);
        if (status is { } s) query = query.Where(j => j.Status == s);
        else query = query.Where(j => j.Status != ImportJobStatus.Archived);
        if (warnings) query = query.Where(j => j.WarningCount > 0 || j.ErrorCount > 0);
        if (!string.IsNullOrWhiteSpace(q)) { var like = $"%{q.Trim()}%"; query = query.Where(j => EF.Functions.ILike(j.Number, like) || EF.Functions.ILike(j.OriginalFileName, like)); }
        pageSize = Math.Clamp(pageSize, 1, 200); page = Math.Max(1, page);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(j => j.SubmittedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => new JobListItem(j.Id, j.Number, j.OriginalFileName, j.FileType, j.ImportMode, j.TargetModule, j.Status, j.Progress, j.TotalRows, j.ImportedRows, j.WarningCount, j.ErrorCount,
                j.SubmittedAt, db.Users.Where(u => u.Id == j.SubmittedById).Select(u => u.DisplayName).First(), db.Sites.Where(x => x.Id == j.SiteId).Select(x => x.Code).First()))
            .ToListAsync(ct);
        return new Page<JobListItem>(items, total, page, pageSize);
    }

    [EnableRateLimiting("processing")]
    [HttpPost]
    [RequestSizeLimit(210L * 1024 * 1024), RequestFormLimits(MultipartBodyLengthLimit = 210L * 1024 * 1024)]
    public async Task<ActionResult<Guid>> Upload(IFormFile file, [FromForm] Guid siteId, [FromForm] Guid? departmentId, [FromForm] string mode, [FromForm] string? targetModule, [FromForm] Guid? targetTemplateId, CancellationToken ct)
    {
        await using var s = file.OpenReadStream();
        var job = await imports.StartAsync(new StartImportRequest(file.FileName, file.ContentType, s, siteId, departmentId, mode, string.IsNullOrWhiteSpace(targetModule) ? null : targetModule, targetTemplateId), ct);
        if (job.FileSize <= imports.InlineBytes) await imports.AdvanceAsync(job, ct);
        return job.Id;
    }

    [HttpGet("{id:guid}")]
    public async Task<JobDetail> Get(Guid id, CancellationToken ct)
    {
        var j = await imports.LoadAsync(id, Domain.Security.Permissions.ImportsView, ct);
        var maps = await db.ImportMappings.AsNoTracking().Where(m => m.ImportJobId == id).OrderBy(m => m.Sequence)
            .Select(m => new MappingDto(m.Id, m.SourceLocation, m.SourceName, m.SourceType, m.TargetField, m.TargetType, m.TransformationRule, (int)m.MappingConfidence, m.UserConfirmed, m.DefaultValue, m.RequiredValue)).ToListAsync(ct);
        var issues = await db.ImportIssues.AsNoTracking().Where(i => i.ImportJobId == id).OrderByDescending(i => i.Severity).ThenBy(i => i.RowNumber).Take(500)
            .Select(i => new IssueDto(i.Id, i.SourceLocation, i.RowNumber, i.Severity, i.IssueCode, i.Description, i.SuggestedResolution, i.UserResolution, i.ResolvedAt)).ToListAsync(ct);
        var hist = await db.ImportHistory.AsNoTracking().Where(h => h.ImportJobId == id).OrderBy(h => h.Id)
            .Select(h => new HistoryDto(h.EventDate, h.Event, h.ActorName, h.PreviousValue, h.NewValue, h.Message)).ToListAsync(ct);
        IReadOnlyList<TargetField> fields = [];
        if (j.TargetModule is not null)
            try { fields = await HttpContext.RequestServices.GetServices<IImportTarget>().First(t => t.Info.Key == j.TargetModule).FieldsAsync(j, ct); } catch (Exception e) when (e is QualGuardIQ.Application.Abstractions.AppException) { }
        var by = await db.Users.Where(u => u.Id == j.SubmittedById).Select(u => u.DisplayName).FirstAsync(ct);
        static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();
        return new JobDetail(j.Id, j.Number, j.SiteId, j.DepartmentId, j.OriginalFileName, j.FileType, j.FileSize, j.FileHash, j.ImportMode, j.TargetModule, j.TargetTemplateId, j.Status, j.Progress,
            j.TotalRows, j.ImportedRows, j.WarningCount, j.ErrorCount, j.SubmittedById, by, j.SubmittedAt, j.StartedAt, j.CompletedAt, j.ApprovedById, j.ApprovedAt, J(j.Settings), J(j.Analysis), J(j.Summary),
            j.RowVersion, maps, issues, hist, fields, j.FileSize > imports.InlineBytes);
    }

    [HttpPut("{id:guid}/settings")] public async Task<IActionResult> Settings(Guid id, CsvSettingsRequest req, CancellationToken ct) { await imports.UpdateSettingsAsync(id, req, ct); return NoContent(); }
    [HttpPut("{id:guid}/excel-settings")] public async Task<IActionResult> ExcelSettings(Guid id, ExcelSettingsRequest req, CancellationToken ct) { await imports.UpdateExcelSettingsAsync(id, req, ct); return NoContent(); }
    [HttpPut("{id:guid}/word-settings")] public async Task<IActionResult> WordSettings(Guid id, WordSettingsRequest req, CancellationToken ct) { await imports.UpdateWordSettingsAsync(id, req, ct); return NoContent(); }
    /// <summary>The original rendered to PDF (Word and Excel through the office conversion adapter), for the side-by-side fidelity comparison.</summary>
    [EnableRateLimiting("processing")]
    [HttpGet("{id:guid}/original-preview")]
    public async Task<IActionResult> OriginalPreview(Guid id, CancellationToken ct)
    {
        var (content, name, type) = await imports.OriginalPreviewAsync(id, ct);
        return File(content, type, name);
    }
    /// <summary>PDF and scanned image settings: pages and table to extract, title, whether pages without text are recognized.</summary>
    [HttpPut("{id:guid}/pdf-settings")] public async Task<IActionResult> PdfSettings(Guid id, PdfSettingsRequest req, CancellationToken ct) { await imports.UpdatePdfSettingsAsync(id, req, ct); return NoContent(); }
    /// <summary>Review of detected fields, tables, checkboxes and signature areas: include or leave out, correct, confirm.</summary>
    [HttpPut("{id:guid}/detections")] public async Task<IActionResult> Detections(Guid id, DetectionReviewRequest req, CancellationToken ct) { await imports.ReviewDetectionsAsync(id, req, ct); return NoContent(); }
    /// <summary>Files derived from the original: rendered page images, recognized text, generated previews.</summary>
    [HttpPut("{id:guid}/conversion-target")] public async Task<IActionResult> ConversionTarget(Guid id, ConversionTargetRequest req, CancellationToken ct) { await imports.SetConversionTargetAsync(id, req, ct); return NoContent(); }
    [HttpGet("conversion-targets")] public IEnumerable<object> ConversionTargets() => QualGuardIQ.Infrastructure.Modules.Imports.Conversion.ConversionEngine.Profiles.Select(p => new { p.Key, p.Name, p.ModuleKey, elements = p.Elements.Select(e => e.Name) });
    [HttpGet("{id:guid}/pipeline")] public async Task<IReadOnlyList<ImportCenterService.PipelineStep>> Pipeline(Guid id, CancellationToken ct) => await imports.PipelineAsync(await imports.LoadAsync(id, Domain.Security.Permissions.ImportsView, ct), ct);
    [HttpPut("{id:guid}/purpose")] public async Task<IActionResult> Purpose(Guid id, PurposeRequest req, CancellationToken ct) { await imports.SetPurposeAsync(id, req, ct); return NoContent(); }
    [HttpGet("{id:guid}/workbench")] public Task<IReadOnlyList<ImportCenterService.WorkbenchStep>> Workbench(Guid id, CancellationToken ct) => imports.WorkbenchAsync(id, ct);
    [HttpGet("{id:guid}/result-package")]
    public async Task<IActionResult> ResultPackage(Guid id, CancellationToken ct) { var (bytes, name) = await imports.ResultPackageAsync(id, ct); return File(bytes, "application/zip", name); }
    [HttpGet("{id:guid}/derived")] public Task<IReadOnlyList<object>> Derived(Guid id, CancellationToken ct) => imports.DerivedFilesAsync(id, ct);
    [HttpGet("{id:guid}/derived/{fileId:guid}")]
    public async Task<IActionResult> DerivedFile(Guid id, Guid fileId, CancellationToken ct)
    {
        var (content, type, name) = await imports.DerivedFileAsync(id, fileId, ct);
        return File(content, type, name);
    }
    /// <summary>One embedded picture of a PDF, as PNG.</summary>
    [HttpGet("{id:guid}/pdf-images/{page:int}/{index:int}")]
    public async Task<IActionResult> PdfImage(Guid id, int page, int index, CancellationToken ct) => File(await imports.PdfImageAsync(id, page, index, ct), "image/png", $"page-{page}-picture-{index}.png");
    /// <summary>Secured grid preview of one worksheet of the preserved original (values, formula, validation and named-range indicators; nothing is run).</summary>
    [HttpGet("{id:guid}/workbook")] public Task<QualGuardIQ.Infrastructure.Modules.Imports.Excel.GridPreview> Workbook(Guid id, [FromQuery] string? sheet, CancellationToken ct) => imports.WorkbookGridAsync(id, sheet, ct);
    [HttpPut("{id:guid}/mapping")] public async Task<IActionResult> Mapping(Guid id, MappingRequest req, CancellationToken ct) { await imports.UpdateMappingAsync(id, req, ct); return NoContent(); }
    [EnableRateLimiting("processing")]
    [HttpPost("{id:guid}/validate")] public Task<DryRunSummary> Validate(Guid id, CancellationToken ct) => imports.ValidateAsync(id, ct);
    [HttpPost("{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id, SubmitRequest req, CancellationToken ct) { await imports.SubmitAsync(id, req.SkipInvalidRows, ct); return NoContent(); }
    [EnableRateLimiting("processing")]
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ApproveImportRequest? req, CancellationToken ct)
    { await imports.ApproveAsync(id, ct, req?.AcceptFidelityReport == true); return NoContent(); }
    [HttpPost("{id:guid}/return")] public async Task<IActionResult> Return(Guid id, ReasonRequest req, CancellationToken ct) { await imports.RejectAsync(id, req.Reason, ct); return NoContent(); }
    [HttpPost("{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await imports.CancelAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/archive")] public async Task<IActionResult> Archive(Guid id, CancellationToken ct) { await imports.ArchiveAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/rollback")] public async Task<IActionResult> Rollback(Guid id, ReasonRequest req, CancellationToken ct) { await imports.RollbackAsync(id, req.Reason, ct); return NoContent(); }
    [HttpPut("{id:guid}/issues/{issueId:guid}")] public async Task<IActionResult> Resolve(Guid id, Guid issueId, ResolveIssueRequest req, CancellationToken ct) { await imports.ResolveIssueAsync(id, issueId, req.Resolution, ct); return NoContent(); }

    [HttpGet("{id:guid}/error-rows")]
    public async Task<IActionResult> ErrorRows(Guid id, CancellationToken ct)
    {
        var job = await imports.LoadAsync(id, Domain.Security.Permissions.ImportsView, ct);
        return File(await imports.ErrorRowsCsvAsync(id, ct), "text/csv", $"{job.Number}-error-rows.csv");
    }

    [HttpGet("{id:guid}/original")]
    public async Task<IActionResult> Original(Guid id, CancellationToken ct)
    {
        var job = await imports.LoadAsync(id, Domain.Security.Permissions.ImportsView, ct);
        return File(await imports.OpenOriginalAsync(job, ct), "application/octet-stream", job.OriginalFileName);
    }
}

public sealed record ApproveImportRequest(bool AcceptFidelityReport);
