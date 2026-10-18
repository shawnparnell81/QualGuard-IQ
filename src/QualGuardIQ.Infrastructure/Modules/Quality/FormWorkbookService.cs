using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

/// <summary>
/// Excel round trip for electronic forms (TECH 6): a generated, protected workbook for controlled offline completion, and its reimport.
/// The reimport checks the template and revision, a server-signed integrity token and the protected structure, takes only the input
/// cells, recalculates through the form engine, keeps the submitted workbook as evidence and refuses a second submission of the same copy.
/// </summary>
public sealed class FormWorkbookService(AppDbContext db, ModuleSupport m, ICurrentUser current, IRecordRegistry records, FormService forms, IFileStorage storage, IDataProtectionProvider dp)
{
    public const string MetaSheet = "_qg";
    const string Purpose = "QualGuardIQ.Forms.OfflineWorkbook.v1";
    IDataProtector Protector => dp.CreateProtector(Purpose);

    public sealed record Placement(string Sheet, int R, int C, string Key, string? Table, string Type, bool Input);
    public sealed record Plan(List<(string Name, string? Table)> Sheets, List<Placement> Cells, Dictionary<string, int> TableRows);
    public sealed record Token(Guid T, Guid R, string H, Guid X);
    public sealed record Submission(Guid RecordId, string RecordNumber, int Values, int TableRows);

    /// <summary>Fields completed in the application rather than offline: calculated values, files and references to other records.</summary>
    static readonly HashSet<string> NotEntered = new(new[] { "calc", "heading", "instruction", "note", "signature", "attachment", "photo" }.Concat(FormDefinitions.ReferenceTypes));

    static string SheetName(string title, HashSet<string> used)
    {
        var n = new string(title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        if (n.Length == 0 || n.StartsWith('_')) n = "Table";
        if (n.Length > 28) n = n[..28];
        var x = n; var i = 2;
        while (!used.Add(x.ToLowerInvariant())) x = $"{n} {i++}";
        return x;
    }

    /// <summary>Deterministic placement of every field and table column; export and reimport both use it.</summary>
    public static Plan Layout(FormDefinition d)
    {
        if (d.Sections.Any(s => s.Repeatable)) throw new AppException("Forms with repeatable sections cannot be completed offline in a workbook yet.");
        var used = new HashSet<string> { MetaSheet.ToLowerInvariant(), "form" };
        var sheets = new List<(string, string?)> { ("Form", null) };
        var cells = new List<Placement>();
        var row = 5;
        var order = d.Sections.Select(s => (string?)s.Key).Append(null).ToList();
        foreach (var sec in order)
        {
            var fields = d.Fields.Where(f => sec is null ? (f.Section is null || d.Sections.All(s => s.Key != f.Section)) : f.Section == sec).Where(f => f.Type is not ("heading" or "instruction" or "note" or "signature")).ToList();
            if (fields.Count == 0) continue;
            row++; // section title
            foreach (var f in fields) cells.Add(new Placement("Form", row++, 2, f.Key, null, f.Type, !NotEntered.Contains(f.Type)));
            row++;
        }
        var tableRows = new Dictionary<string, int>();
        foreach (var t in d.Tables)
        {
            var name = SheetName(t.Title.Length > 0 ? t.Title : t.Key, used);
            sheets.Add((name, t.Key));
            var n = Math.Clamp(Math.Max(Math.Max(t.MinRows, t.FixedRows?.Count ?? 0), 50), 1, Math.Max(1, t.MaxRows));
            tableRows[t.Key] = n;
            for (var c = 0; c < t.Columns.Count; c++) cells.Add(new Placement(name, 1, c + 1, t.Columns[c].Key, t.Key, t.Columns[c].Type, !NotEntered.Contains(t.Columns[c].Type)));
        }
        return new Plan(sheets, cells, tableRows);
    }

    /// <summary>Hash of the protected structure: sheet names and order, every locked cell's text, and where the input cells are.</summary>
    public static string StructureHash(IXLWorkbook wb)
    {
        var sb = new StringBuilder();
        foreach (var ws in wb.Worksheets.Where(w => w.Name != MetaSheet).OrderBy(w => w.Position))
        {
            sb.Append("#S:").Append(ws.Name).Append('\n');
            foreach (var c in ws.CellsUsed(XLCellsUsedOptions.All).OrderBy(c => c.Address.RowNumber).ThenBy(c => c.Address.ColumnNumber))
            {
                if (c.Style.Protection.Locked)
                {
                    var t = c.HasFormula ? "=" + c.FormulaA1 : c.GetString();
                    if (t.Length > 0) sb.Append(c.Address.ToString()).Append('=').Append(t).Append('\n');
                }
                else sb.Append(c.Address.ToString()).Append(":in\n");
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    async Task<(ControlledDocument Doc, DocumentRevision Rev, FormDefinition Def)> EffectiveAsync(Guid templateId, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == templateId && d.Kind == DocumentKind.FormTemplate, ct) ?? throw new NotFoundException("Form template");
        await records.RequireViewAsync(DocumentRecordTypes.FormTemplate, doc.Id, ct);
        var rev = await db.DocumentRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.DocumentId == doc.Id && r.Status == RevisionStatus.Effective, ct)
            ?? throw new AppException("This form template has no effective revision.");
        return (doc, rev, FormDefinitions.Parse(rev.FieldSchema!));
    }

    public async Task<(byte[] Bytes, string FileName)> ExportAsync(Guid templateId, CancellationToken ct)
    {
        var (doc, rev, def) = await EffectiveAsync(templateId, ct);
        var plan = Layout(def);
        using var wb = new XLWorkbook();
        var brand = XLColor.FromHtml("#1257C4");
        var form = wb.AddWorksheet("Form");
        form.Column(1).Width = 38; form.Column(2).Width = 34; form.Column(3).Width = 40;
        form.Range(1, 1, 1, 3).Merge().Value = doc.Title;
        form.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(brand);
        form.Cell(2, 1).Value = $"{doc.Number} revision {rev.Revision} · offline copy · complete the white cells only";
        form.Cell(2, 1).Style.Font.SetItalic();
        var bySection = def.Fields.ToDictionary(f => f.Key);
        int? lastRow = null;
        foreach (var p in plan.Cells.Where(p => p.Table is null))
        {
            var f = bySection[p.Key];
            if (lastRow is null || p.R != lastRow + 1)
            {
                var title = def.Sections.FirstOrDefault(s => s.Key == f.Section)?.Title ?? "General";
                var h = form.Range(p.R - 1, 1, p.R - 1, 3); h.Merge().Value = title;
                h.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#E3ECFA"));
            }
            lastRow = p.R;
            form.Cell(p.R, 1).Value = f.Label + (f.Required ? " *" : "");
            var cell = form.Cell(p.R, 2);
            Input(cell, f, p.Input);
            if (f.Help is { Length: > 0 } help) form.Cell(p.R, 3).Value = help;
            if (!p.Input) form.Cell(p.R, 3).Value = f.Type == "calc" ? "Calculated in QualGuard IQ when the workbook is imported." : "Completed in QualGuard IQ after the workbook is imported.";
        }
        foreach (var (name, key) in plan.Sheets.Where(s => s.Table is not null))
        {
            var t = def.Tables.Single(x => x.Key == key);
            var ws = wb.AddWorksheet(name);
            var n = plan.TableRows[t.Key];
            for (var c = 0; c < t.Columns.Count; c++)
            {
                var col = t.Columns[c];
                var h = ws.Cell(1, c + 1); h.Value = col.Label + (col.Required ? " *" : "");
                h.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(brand).Alignment.SetWrapText();
                ws.Column(c + 1).Width = Math.Clamp(col.Label.Length + 6, 12, 40);
                var body = ws.Range(2, c + 1, n + 1, c + 1);
                foreach (var cell in body.Cells()) Input(cell, col, !NotEntered.Contains(col.Type));
            }
            ws.SheetView.FreezeRows(1);
        }
        var meta = wb.AddWorksheet(MetaSheet);
        var hash = StructureHash(wb);
        var token = new Token(doc.Id, rev.Id, hash, Guid.NewGuid());
        meta.Cell(1, 1).Value = Protector.Protect(JsonSerializer.Serialize(token));
        meta.Cell(2, 1).Value = "Integrity data for the offline copy. Do not change.";
        meta.Visibility = XLWorksheetVisibility.VeryHidden;
        var pw = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        foreach (var ws in wb.Worksheets) ws.Protect(pw).AllowElement(XLSheetProtectionElements.FormatColumns | XLSheetProtectionElements.FormatRows);
        wb.Protect(pw);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        db.AuditEntries.Add(new AuditEntry { OccurredAt = m.Now, UserId = current.UserId, UserName = current.UserName, Action = "Exported", EntityType = nameof(DocumentRevision), EntityId = rev.Id,
            SiteId = doc.SiteId, Description = $"Offline workbook of form template {doc.Number} rev {rev.Revision} generated (copy {token.X})" });
        await db.SaveChangesAsync(ct);
        return (ms.ToArray(), $"{doc.Number}-rev-{rev.Revision}-offline.xlsx");
    }

    static void Input(IXLCell cell, FormField f, bool input)
    {
        cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(XLColor.FromHtml("#9AA9C2"));
        if (!input) { cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F1F3F6")); cell.Style.Protection.SetLocked(true); return; }
        cell.Style.Protection.SetLocked(false);
        switch (f.Type)
        {
            case "choice" or "select" or "radio" when f.Options is { Count: > 0 } o && string.Join(",", o).Length < 250:
                cell.CreateDataValidation().List("\"" + string.Join(",", o.Select(x => x.Replace("\"", "").Replace(",", " "))) + "\"", true);
                break;
            case "boolean" or "checkbox":
                cell.CreateDataValidation().List("\"Yes,No\"", true);
                break;
            case "passfail":
                cell.CreateDataValidation().List("\"pass,fail,na\"", true);
                break;
            case "date":
                cell.Style.NumberFormat.Format = "yyyy-mm-dd";
                cell.CreateDataValidation().Date.GreaterThan(new DateTime(1900, 1, 1));
                break;
            case "integer":
                cell.CreateDataValidation().WholeNumber.Between((int)Math.Max(int.MinValue, f.Min ?? int.MinValue), (int)Math.Min(int.MaxValue, f.Max ?? int.MaxValue));
                break;
            case "number" or "currency" or "percent" or "measurement":
                cell.CreateDataValidation().Decimal.Between((double)(f.Min ?? -1e15m), (double)(f.Max ?? 1e15m));
                break;
        }
    }

    static string? Read(IXLCell c, string type)
    {
        if (c.IsEmpty()) return null;
        var v = c.Value;
        switch (type)
        {
            case "date":
                if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (v.IsNumber) return DateTime.FromOADate(v.GetNumber()).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            case "datetime":
                if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
                break;
            case "boolean" or "checkbox":
                if (v.IsBoolean) return v.GetBoolean() ? "true" : "false";
                var s = c.GetString().Trim().ToLowerInvariant();
                return s is "yes" or "true" or "y" or "x" ? "true" : s is "no" or "false" or "n" ? "false" : s;
        }
        if (v.IsNumber) return v.GetNumber().ToString("0.###############", CultureInfo.InvariantCulture);
        var text = c.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public async Task<Submission> ImportAsync(Guid templateId, Stream file, string fileName, Guid siteId, Guid? departmentId, CancellationToken ct)
    {
        var (doc, rev, def) = await EffectiveAsync(templateId, ct);
        await m.RequireAsync(Permissions.FormsEdit, siteId, departmentId, ct);
        var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        if (ms.Length is 0 or > 50L * 1024 * 1024) throw new AppException("The workbook must be between 1 byte and 50 MB.");
        ms.Position = 0;
        var problem = await Imports.ImportCenterService.InspectSecurityAsync(ms, ".xlsx", ct);
        if (problem is not null) throw new AppException(problem);
        var bytes = ms.ToArray();
        var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        XLWorkbook wb;
        try { ms.Position = 0; wb = new XLWorkbook(ms); }
        catch (Exception e) when (e is not OutOfMemoryException) { throw new AppException($"The workbook could not be read: {e.Message}"); }
        using (wb)
        {
            if (!wb.TryGetWorksheet(MetaSheet, out var meta)) throw new AppException("This workbook was not generated by QualGuard IQ for offline completion.");
            Token token;
            try { token = JsonSerializer.Deserialize<Token>(Protector.Unprotect(meta.Cell(1, 1).GetString()))!; }
            catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { throw new AppException("The workbook's integrity data is missing or was changed."); }
            if (token.T != doc.Id) throw new AppException("The workbook belongs to a different form template.");
            if (token.R != rev.Id)
            {
                var label = await db.DocumentRevisions.Where(r => r.Id == token.R).Select(r => r.Revision).FirstOrDefaultAsync(ct);
                throw new AppException($"The workbook was generated from revision {label ?? "?"}, which is no longer the effective revision. Download a new copy.");
            }
            if (StructureHash(wb) != token.H) throw new AppException("The protected parts of the workbook were changed (labels, headings, sheets or which cells take input). Use an unaltered copy.");
            var marker = $"Offline submission {token.X}";
            if (await db.Attachments.AnyAsync(a => a.OwnerType == FormService.RecordType && (a.Description == marker || (a.Sha256 == sha && a.Category == "Offline workbook")), ct))
                throw new ConflictException("This offline copy has already been submitted.");

            var plan = Layout(def);
            var values = new Dictionary<string, string?>();
            foreach (var p in plan.Cells.Where(p => p.Table is null && p.Input)) values[p.Key] = Read(wb.Worksheet(p.Sheet).Cell(p.R, p.C), p.Type);
            var rowsTotal = 0;
            foreach (var (name, key) in plan.Sheets.Where(s => s.Table is not null))
            {
                var ws = wb.Worksheet(name);
                var cols = plan.Cells.Where(p => p.Table == key && p.Input).ToList();
                var arr = new JsonArray();
                for (var r = 2; r <= plan.TableRows[key!] + 1; r++)
                {
                    var o = new JsonObject();
                    foreach (var p in cols) if (Read(ws.Cell(r, p.C), p.Type) is { } v) o[p.Key] = v;
                    if (o.Count > 0) arr.Add(o);
                }
                rowsTotal += arr.Count;
                values[key!] = arr.ToJsonString();
            }

            var ownTx = db.Database.CurrentTransaction is null;
            await using var tx = ownTx ? await db.Database.BeginTransactionAsync(ct) : null;
            var rec = await forms.StartAsync(new StartFormRequest(doc.Id, siteId, departmentId, $"{doc.Title} (offline)"), ct);
            var saved = await forms.SaveAsync(rec.Id, values, (await db.FormRecords.AsNoTracking().FirstAsync(r => r.Id == rec.Id, ct)).RowVersion, ct);
            var a = new Attachment
            {
                OwnerType = FormService.RecordType, OwnerId = rec.Id, SiteId = siteId, DepartmentId = departmentId, ViewPermission = Permissions.FormsView,
                FileName = Path.GetFileName(fileName) is { Length: > 0 and <= 255 } fn ? fn : "offline.xlsx", ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                SizeBytes = bytes.Length, Sha256 = sha, Category = "Offline workbook", Description = marker, IsLocked = true, ScanStatus = ScanStatus.NotScanned,
            };
            a.LineageId = a.Id;
            a.StorageKey = $"formrecord/{rec.Id:N}/{a.Id:N}";
            await storage.PutAsync(a.StorageKey, new MemoryStream(bytes), a.ContentType, ct);
            db.Attachments.Add(a);
            db.AuditEntries.Add(new AuditEntry { OccurredAt = m.Now, UserId = current.UserId, UserName = current.UserName, Action = "Imported", EntityType = "FormRecord", EntityId = rec.Id,
                SiteId = siteId, Description = $"Form record {rec.Number} created from offline workbook copy {token.X} (template {doc.Number} rev {rev.Revision})" });
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
            return new Submission(rec.Id, rec.Number, saved.Count(v => v.Value is not null), rowsTotal);
        }
    }
}
