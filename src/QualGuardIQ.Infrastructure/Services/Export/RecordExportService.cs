using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Sequfloh;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Services.Export;

public sealed record ExportFile(byte[] Content, string ContentType, string FileName);

/// <summary>A list as the person sees it on screen: its title, the visible columns and the rows after their filters and sorting.</summary>
public sealed record TableExportRequest(string Title, string Format, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string?>> Rows, string? Filters = null);

/// <summary>
/// Print, PDF and file exports for every record and list. Records are written from their stored fields plus the shared panels (additional
/// fields, actions, approvals, files, comments and history); form records and Sequfloh documents use their own print layouts for PDF.
/// Every export is written to the audit trail.
/// </summary>
public sealed class RecordExportService(AppDbContext db, IRecordRegistry records, ICurrentUser current, IClock clock, ISiteCalendar calendar, RecordTypeLabels labels,
    CustomFieldService customFields, IServiceProvider services)
{
    public const int MaxRows = 20000, MaxColumns = 60, MaxCell = 10000;

    static readonly string[] Hidden = ["Id", "RowVersion", "CreatedById", "UpdatedById", "SiteId", "DepartmentId"];
    static readonly string[] Sensitive = ["secret", "password", "token", "hash", "salt", "apikey", "privatekey"];

    async Task<List<ExportField>> HeaderAsync(Guid? siteId, CancellationToken ct)
    {
        var org = await db.OrganizationSettings.AsNoTracking().Select(s => s.OrganizationName).FirstOrDefaultAsync(ct) ?? "";
        var zone = await calendar.ZoneAsync(siteId, ct);
        var now = TimeZoneInfo.ConvertTime(clock.UtcNow, zone);
        var list = new List<ExportField>();
        if (org.Length > 0) list.Add(new("Organization", org));
        list.Add(new("Printed by", current.UserName));
        list.Add(new("Printed on", $"{now:yyyy-MM-dd HH:mm} ({zone.Id})"));
        return list;
    }

    public async Task<ExportFile> TableAsync(TableExportRequest req, CancellationToken ct)
    {
        if (!ExportRenderer.TryParse(req.Format, out var format)) throw new AppException("Choose CSV, Excel, Word or PDF.");
        var title = string.IsNullOrWhiteSpace(req.Title) ? "Export" : req.Title.Trim();
        if (title.Length > 200) title = title[..200];
        if (req.Columns is null || req.Columns.Count == 0) throw new AppException("There are no columns to export.");
        if (req.Columns.Count > MaxColumns) throw new AppException($"Exports are limited to {MaxColumns} columns.");
        if ((req.Rows?.Count ?? 0) > MaxRows) throw new AppException($"Exports are limited to {MaxRows:N0} rows. Narrow the list with its filters first.");
        static string Cut(string? s) => s is null ? "" : s.Length > MaxCell ? s[..MaxCell] : s;
        var rows = (req.Rows ?? []).Select(r => (IReadOnlyList<string>)req.Columns.Select((_, i) => i < r.Count ? Cut(r[i]) : "").ToList()).ToList();
        var header = await HeaderAsync(null, ct);
        if (!string.IsNullOrWhiteSpace(req.Filters)) header.Add(new("Filters", Cut(req.Filters)));
        header.Add(new("Rows", rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var doc = new ExportDocument(title, null, header, [new ExportSection(title, Columns: req.Columns.Select(Cut).ToList(), Rows: rows)]);
        AuditLog.Add(db, current, clock.UtcNow, "Exported", "List", Guid.Empty, null,
            $"Exported the list \"{title}\" ({rows.Count} rows) as {ExportRenderer.Extension(format).ToUpperInvariant()}{(string.IsNullOrWhiteSpace(req.Filters) ? "" : $", filtered by {req.Filters}")}");
        await db.SaveChangesAsync(ct);
        return new ExportFile(ExportRenderer.Render(doc, format), ExportRenderer.ContentType(format), ExportRenderer.FileName(title, format));
    }

    public async Task<ExportFile> RecordAsync(string type, Guid id, string formatText, CancellationToken ct)
    {
        if (!ExportRenderer.TryParse(formatText, out var format)) throw new AppException("Choose CSV, Excel, Word or PDF.");
        var r = await records.RequireViewAsync(type, id, ct);
        if (format == ExportFormat.Pdf && await OwnLayoutAsync(r, ct) is { } own) return own;
        var doc = await BuildAsync(r, ct);
        AuditLog.Add(db, current, clock.UtcNow, format == ExportFormat.Pdf ? "Printed" : "Exported", r.Type, r.Id, r.SiteId,
            $"{(format == ExportFormat.Pdf ? "Printed" : "Exported")} {labels.For(r.Type)} {r.Number} as {ExportRenderer.Extension(format).ToUpperInvariant()}");
        await db.SaveChangesAsync(ct);
        return new ExportFile(ExportRenderer.Render(doc, format), ExportRenderer.ContentType(format), ExportRenderer.FileName($"{r.Number} {r.Title}", format));
    }

    /// <summary>Form records and Sequfloh documents already have controlled print layouts; their PDF comes from those (which audit the print themselves).</summary>
    async Task<ExportFile?> OwnLayoutAsync(RecordInfo r, CancellationToken ct)
    {
        if (r.Type == FormService.RecordType)
        {
            var p = await services.GetRequiredService<FormPrintService>().RecordAsync(r.Id, null, ct);
            return new ExportFile(p.Pdf, "application/pdf", p.FileName);
        }
        if (r.Type == SequflohInstructionService.EntityType)
        {
            var (pdf, name) = await services.GetRequiredService<SequflohPrintService>().InstructionAsync(r.Id, ct);
            return new ExportFile(pdf, "application/pdf", name);
        }
        if (r.Type == SequflohTrackingService.EntityType)
        {
            var (pdf, name) = await services.GetRequiredService<SequflohPrintService>().TrackingAsync(r.Id, ct);
            return new ExportFile(pdf, "application/pdf", name);
        }
        return null;
    }

    /// <summary>The record as sections: summary, its stored details, additional fields, actions, approvals, files, comments and history.</summary>
    public async Task<ExportDocument> BuildAsync(RecordInfo r, CancellationToken ct)
    {
        var zone = await calendar.ZoneAsync(r.SiteId, ct);
        string T(object? v) => ExportRenderer.Text(v, zone);
        var site = await db.Sites.AsNoTracking().Where(s => s.Id == r.SiteId).Select(s => s.Code + " " + s.Name).FirstOrDefaultAsync(ct);
        var dept = r.DepartmentId is { } d ? await db.Departments.AsNoTracking().Where(x => x.Id == d).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var summary = new List<ExportField> { new("Number", r.Number), new("Title", r.Title), new("Type", labels.For(r.Type)) };
        if (r.Status is { Length: > 0 }) summary.Add(new("Status", RecordTypeLabels.Humanize(r.Status)));
        if (site is not null) summary.Add(new("Site", site));
        if (dept is not null) summary.Add(new("Department", dept));
        var sections = new List<ExportSection> { new("Summary", summary) };

        if (records.EntityTypeOf(r.Type) is { } clr && await db.FindAsync(clr, r.Id) is { } entity)
        {
            var et = db.Model.FindEntityType(entity.GetType()) ?? db.Model.FindEntityType(clr)!;
            var props = et.GetProperties().Where(p => p.PropertyInfo is not null && !Hidden.Contains(p.Name) && !p.IsPrimaryKey()).ToList();
            var guids = props.Where(p => p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?)).Select(p => p.PropertyInfo!.GetValue(entity)).OfType<Guid>().Distinct().ToList();
            var people = guids.Count == 0 ? new Dictionary<Guid, string>() : await db.Users.AsNoTracking().Where(u => guids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
            var fields = new List<ExportField>();
            foreach (var p in props)
            {
                var name = p.Name;
                var lower = name.ToLowerInvariant();
                if (Sensitive.Any(lower.Contains) || lower.EndsWith("json") || lower.EndsWith("schema") || p.ClrType == typeof(byte[])) continue;
                var value = p.PropertyInfo!.GetValue(entity);
                if (value is Guid g)
                {
                    // only references to people are readable; other ids are covered by the record's links
                    if (!people.TryGetValue(g, out var who)) continue;
                    fields.Add(new(Label(name.EndsWith("Id") ? name[..^2] : name), who));
                    continue;
                }
                if (name is nameof(Domain.Common.Entity.CreatedAt) or nameof(Domain.Common.Entity.UpdatedAt)) continue;
                var text = T(value);
                if (text.Length > MaxCell) text = text[..MaxCell];
                fields.Add(new(Label(name), text));
            }
            if (entity is Domain.Common.Entity en)
            {
                fields.Add(new("Created", T(en.CreatedAt)));
                fields.Add(new("Last changed", T(en.UpdatedAt)));
            }
            sections.Add(new("Details", fields));
        }

        var (defs, values) = await customFields.GetAsync(r.Type, r.Id, ct);
        if (defs.Count > 0) sections.Add(new("Additional fields", defs.Select(f => new ExportField(f.Label, values.GetValueOrDefault(f.Key) ?? "")).ToList()));

        var actions = await (from a in db.ActionItems.AsNoTracking() where a.SourceType == r.Type && a.SourceId == r.Id
                             join u in db.Users on a.AssigneeId equals u.Id into uu from u in uu.DefaultIfEmpty()
                             orderby a.Number select new { a.Number, a.Title, a.ActionType, Assignee = u != null ? u.DisplayName : "", a.DueDate, a.Status, a.CompletionNotes }).ToListAsync(ct);
        if (actions.Count > 0)
            sections.Add(new("Actions", Columns: ["Number", "Title", "Type", "Assignee", "Due", "Status", "Completion notes"],
                Rows: actions.Select(a => (IReadOnlyList<string>)[a.Number, a.Title, a.ActionType, a.Assignee, T(a.DueDate), T(a.Status), a.CompletionNotes ?? ""]).ToList()));

        var decisions = await db.ApprovalRequests.AsNoTracking().Where(a => a.RecordType == r.Type && a.RecordId == r.Id).SelectMany(a => a.Decisions)
            .OrderBy(x => x.SignedAt).Select(x => new { x.StepName, x.SignerName, x.Approved, x.Meaning, x.Comment, x.SignedAt }).ToListAsync(ct);
        if (decisions.Count > 0)
            sections.Add(new("Approvals and signatures", Columns: ["Step", "Signed by", "Decision", "Meaning", "Comment", "Signed"],
                Rows: decisions.Select(x => (IReadOnlyList<string>)[x.StepName, x.SignerName, x.Approved ? "Approved" : "Rejected", x.Meaning, x.Comment ?? "", T(x.SignedAt)]).ToList()));

        var files = await db.Attachments.AsNoTracking().Where(a => a.OwnerType == r.Type && a.OwnerId == r.Id && !a.IsArchived)
            .OrderBy(a => a.FileName).Select(a => new { a.FileName, a.Version, a.SizeBytes, a.CreatedAt }).ToListAsync(ct);
        if (files.Count > 0)
            sections.Add(new("Files", Columns: ["File", "Version", "Size", "Uploaded"],
                Rows: files.Select(f => (IReadOnlyList<string>)[f.FileName, T(f.Version), $"{f.SizeBytes / 1024.0:0.0} KB", T(f.CreatedAt)]).ToList()));

        var comments = await db.Comments.AsNoTracking().Where(c => c.OwnerType == r.Type && c.OwnerId == r.Id).OrderBy(c => c.CreatedAt)
            .Select(c => new { c.AuthorName, c.Body, c.CreatedAt }).ToListAsync(ct);
        if (comments.Count > 0)
            sections.Add(new("Comments", Columns: ["When", "Who", "Comment"], Rows: comments.Select(c => (IReadOnlyList<string>)[T(c.CreatedAt), c.AuthorName, c.Body]).ToList()));

        var history = await db.AuditEntries.AsNoTracking().Where(a => a.EntityId == r.Id).OrderBy(a => a.Id).Take(500)
            .Select(a => new { a.OccurredAt, a.UserName, a.Action, a.Description }).ToListAsync(ct);
        sections.Add(new("History", Columns: ["When", "Who", "What", "Description"],
            Rows: history.Select(h => (IReadOnlyList<string>)[T(h.OccurredAt), h.UserName, h.Action, h.Description]).ToList()));

        return new ExportDocument($"{r.Number} {r.Title}".Trim(), labels.For(r.Type), await HeaderAsync(r.SiteId, ct), sections);
    }

    /// <summary>A property name as a label: "DetectedOn" becomes "Detected on", "PartNumber" becomes "Part number" (quality acronyms kept).</summary>
    public static string Label(string name)
    {
        var words = RecordTypeLabels.Humanize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < words.Length; i++) if (words[i].Any(char.IsLower)) words[i] = words[i].ToLowerInvariant();
        return string.Join(' ', words);
    }
}
