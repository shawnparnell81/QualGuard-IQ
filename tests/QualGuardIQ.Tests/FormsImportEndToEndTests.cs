using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>
/// TECH 19 end-to-end tests 1–6 at API level, one numbered assertion block per step of the prompt
/// (the browser runs of the same flows are e2e34–e2e38 and e2e41).
/// </summary>
[Collection("api")]
public sealed class FormsImportEndToEndTests(TestApp app)
{
    private const string Pw = "long-enough-password";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    static async Task<HttpResponseMessage> UploadRawAsync(ApiClient c, Guid site, string name, byte[] bytes, string mode, string? target = null, string type = Xlsx)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent(mode), "mode");
        if (target is not null) form.Add(new StringContent(target), "targetModule");
        return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
    }

    static async Task<Guid> UploadAsync(ApiClient c, Guid site, string name, byte[] bytes, string mode, string? target = null, string type = Xlsx)
    {
        var r = await UploadRawAsync(c, site, name, bytes, mode, target, type);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    static int Status(JsonElement job) => job.GetProperty("status").GetInt32();
    static List<JsonElement> Issues(JsonElement job) => [.. job.GetProperty("issues").EnumerateArray()];

    async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> q)
    {
        using var scope = app.Services.CreateScope();
        return await q(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    static async Task PublishTemplateAsync(ApiClient author, ApiClient approver, Guid docId)
    {
        var doc = await author.GetDocumentAsync(docId);
        var rev = doc.Revisions.Single(r => r.Status == 0);
        var sub = await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit");
        Assert.True(sub.StatusCode == HttpStatusCode.NoContent, await sub.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(doc.Number))).StatusCode);
    }

    /// <summary>END-TO-END TEST 1: WORD FORM IMPORT.</summary>
    [Fact]
    public async Task E2E1_word_form_is_imported_published_completed_printed_and_linked_to_its_source_revision()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);

        // 1. Upload complex DOCX (tables, merged cells, content controls, checkbox, landscape section, picture).
        var id = await UploadAsync(author, site, "First article report.docx", DocxBuilder.FirstArticleReport(), "template", type: Docx);

        // 2. Scan file: the original is hashed, locked and scanned before anything reads it.
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.NotEqual((int)ImportJobStatus.SecurityScanFailed, Status(job));
        var hash = job.GetProperty("fileHash").GetString()!;
        Assert.Equal(64, hash.Length);
        Assert.Contains(job.GetProperty("history").EnumerateArray(), h => h.GetProperty("event").GetString() == "StatusChanged" && h.GetProperty("newValue").GetString() == "Inspecting");
        Assert.True(await DbAsync(db => db.Attachments.AnyAsync(a => a.OwnerId == id && a.Category == "Import original" && a.IsLocked && a.Sha256 == hash)));

        // 3. Parse structure.
        Assert.Equal((int)ImportJobStatus.PreviewReady, Status(job));
        var docSummary = job.GetProperty("analysis").GetProperty("document");
        Assert.Equal("First Article Inspection Report", docSummary.GetProperty("title").GetString());
        Assert.Equal(3, docSummary.GetProperty("tables").GetArrayLength());
        Assert.Equal(["portrait", "landscape"], docSummary.GetProperty("orientations").EnumerateArray().Select(x => x.GetString()));

        // 4. Generate preview: the converted definition with its original-look layout, and the pipeline reaches the preview step.
        var conv = job.GetProperty("analysis").GetProperty("conversion");
        var def = FormDefinitions.Parse(conv.GetProperty("definition").GetRawText());
        Assert.Equal("docx", def.Layout!.Source);
        var pipeline = (await author.GetElAsync($"/api/imports/{id}/pipeline")).EnumerateArray().ToList();
        Assert.Equal(23, pipeline.Count);

        // 5. Detect fields: content controls become typed fields, the checkbox stays a checkbox, the dropdown a dropdown.
        var detections = job.GetProperty("analysis").GetProperty("detections").EnumerateArray().ToList();
        Assert.Contains(detections, d => d.GetProperty("kind").GetString() == "checkbox");
        Assert.Contains(detections, d => d.GetProperty("kind").GetString() == "table");
        Assert.Contains(def.Fields, f => f.Key == "customer_approval" && f.Type == "boolean");
        Assert.Contains(def.Fields, f => f.Key == "reason" && f.Options is { Count: > 0 });

        // 6. Correct mappings: rename a detected field and confirm the review.
        var supplier = detections.First(d => d.GetProperty("kind").GetString() == "field" && d.GetProperty("id").GetString()!.EndsWith("supplier_name"));
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/detections", new { items = new object[] { new { id = supplier.GetProperty("id").GetString(), label = "Supplier legal name" } }, confirmAll = true })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        def = FormDefinitions.Parse(job.GetProperty("analysis").GetProperty("conversion").GetProperty("definition").GetRawText());
        Assert.Equal("Supplier legal name", def.Fields.Single(f => f.Key == "supplier_name").Label);

        // 7. Compare fidelity: the report lists what was kept and what was not (the picture), the original renders for the side-by-side view.
        var report = job.GetProperty("analysis").GetProperty("conversion").GetProperty("report");
        Assert.Equal("docx", report.GetProperty("source").GetString());
        Assert.Contains(Issues(job), i => i.GetProperty("issueCode").GetString() == "FIDELITY" && i.GetProperty("description").GetString()!.Contains("picture"));

        // 8. Submit for approval; the approver must accept the fidelity report.
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = true })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        var docId = Guid.Parse(job.GetProperty("summary").GetProperty("documentId").GetString()!);

        // 9. Publish template through the form template approval route.
        await PublishTemplateAsync(author, approver, docId);
        var published = await author.GetDocumentAsync(docId);
        var rev = published.Revisions.Single();
        Assert.Equal((int)RevisionStatus.Effective, rev.Status);

        // 10. Complete electronic form.
        var recId = (await (await author.PostOkAsync("/api/forms/records", new { templateId = docId, siteId = site, departmentId = (Guid?)null, title = (string?)null })).JsonAsync()).Id();
        var rec = await author.GetElAsync($"/api/forms/records/{recId}");
        var saved = await author.PutAsync($"/api/forms/records/{recId}", new { values = new Dictionary<string, string?> { ["supplier_name"] = "Precision Castings Ltd", ["material"] = "AL 6061-T6", ["customer_approval"] = "true" }, rowVersion = rec.GetProperty("rowVersion").GetUInt32() });
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        // 11. Generate PDF.
        var pdf = await author.GetAsync($"/api/forms/records/{recId}/pdf");
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));

        // 12. Confirm historical link to source revision: the record stays on the revision it was completed on, which keeps the original file.
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/documents/{docId}/revisions", new { revision = (string?)null, changeSummary = "Add a sample plan field" })).StatusCode);
        var (recRev, srcHash) = await DbAsync(async db =>
        {
            var r = await db.FormRecords.AsNoTracking().SingleAsync(x => x.Id == recId);
            var src = await db.DocumentRevisions.AsNoTracking().Where(x => x.Id == r.TemplateRevisionId).Select(x => x.SourceFileId).SingleAsync();
            return (r.TemplateRevisionId, await db.Attachments.AsNoTracking().Where(a => a.Id == src).Select(a => a.Sha256).SingleAsync());
        });
        Assert.Equal(rev.Id, recRev);
        Assert.Equal(hash, srcHash);
    }

    static byte[] CalibrationWorkbook(string code, string next, string past)
    {
        using var wb = new XLWorkbook();
        var cover = wb.AddWorksheet("Summary");
        cover.Cell("A1").Value = "Calibration master list";
        cover.Cell("A3").Value = "Instruments"; cover.Cell("B3").FormulaA1 = "COUNTA(Equipment!A2:A20)";
        cover.Cell("A4").Value = "Average frequency"; cover.Cell("B4").FormulaA1 = "AVERAGE(Equipment!G2:G20)";
        var ws = wb.AddWorksheet("Equipment");
        string[] head = ["Equipment ID", "Equipment name", "Type", "Manufacturer", "Serial number", "Accuracy", "Frequency (days)", "Last calibration", "Next due date", "Provider", "Status"];
        for (var i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        object[][] rows =
        [
            [$"{code}-G1", "Outside micrometer", "Micrometer", "Mitutoyo", "M-1", "±0.002 mm", 365, "2026-02-01", next, "Internal lab", "Active"],
            [$"{code}-G2", "Pin gauge set", "Gauge", "Vermont", "PG-7", "±0.001 mm", 730, "2025-11-01", next, "Acme Calibration", "Active"],
            [$"{code}-G3", "Surface plate", "Plate", "Starrett", "SP-2", "Grade A", 365, "not a date", next, "Internal lab", "Active"],
            [$"{code}-G4", "Bore gauge", "Gauge", "Sunnen", "BG-4", "±0.005 mm", 365, "2025-01-01", past, "Internal lab", "Active"],
            [$"{code}-G1", "Outside micrometer", "Micrometer", "Mitutoyo", "M-1", "±0.002 mm", 365, "2026-02-01", next, "Internal lab", "Active"],
        ];
        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < head.Length; c++)
                if (rows[r][c] is int n) ws.Cell(r + 2, c + 1).Value = n; else ws.Cell(r + 2, c + 1).Value = (string)rows[r][c];
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    /// <summary>END-TO-END TEST 2: EXCEL CALIBRATION MASTER LIST.</summary>
    [Fact]
    public async Task E2E2_excel_calibration_master_list_is_migrated_into_equipment_with_the_original_workbook_kept()
    {
        var code = "EC" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var site = await app.CreateSiteAsync(code);
        var (_, author) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.CalibrationEdit, Permissions.ImportsView, Permissions.ImportsEdit, Permissions.ImportsDownloadOriginal], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var next = TestDates.Today.AddDays(120).ToString("yyyy-MM-dd");
        var past = TestDates.Today.AddDays(-30).ToString("yyyy-MM-dd");
        var bytes = CalibrationWorkbook(code, next, past);

        // 1. Upload workbook.
        var id = await UploadAsync(author, site, "calibration master list.xlsx", bytes, "master-list", "master-equipment");

        // 2. Detect worksheets.
        var job = await author.GetElAsync($"/api/imports/{id}");
        var sheets = job.GetProperty("analysis").GetProperty("workbook").GetProperty("sheets").EnumerateArray().Select(x => x.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Summary", "Equipment"], sheets);

        // 3. Select master-list worksheet.
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/excel-settings", new { sheet = "Equipment" })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal(5, job.GetProperty("totalRows").GetInt32());

        // 4. Preserve original workbook: locked, hashed, byte for byte.
        var hash = job.GetProperty("fileHash").GetString()!;
        Assert.True(await DbAsync(db => db.Attachments.AnyAsync(a => a.OwnerId == id && a.Category == "Import original" && a.IsLocked && a.Sha256 == hash)));

        // 5. Map equipment fields.
        var maps = job.GetProperty("mappings").EnumerateArray().ToDictionary(m => m.GetProperty("sourceName").GetString()!, m => m.GetProperty("targetField").ValueKind == JsonValueKind.String ? m.GetProperty("targetField").GetString() : null);
        Assert.Equal("number", maps["Equipment ID"]);
        Assert.Equal("name", maps["Equipment name"]);
        Assert.Equal("frequency", maps["Frequency (days)"]);
        Assert.Equal("last", maps["Last calibration"]);
        Assert.Equal("next", maps["Next due date"]);

        // 6. Validate dates, 7. detect duplicate equipment, 8. preview changes, 9. dry run.
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(5, dry.GetProperty("rows").GetInt32());
        Assert.Equal(2, dry.GetProperty("valid").GetInt32());
        Assert.Equal(2, dry.GetProperty("inError").GetInt32()); // the unreadable date and the overdue active gauge
        Assert.Equal(1, dry.GetProperty("duplicates").GetInt32());
        var preview = dry.GetProperty("preview").EnumerateArray().ToList();
        Assert.Contains(preview, r => r.GetProperty("problems").EnumerateArray().Any(p => p.GetString()!.Contains("Last calibration")));
        Assert.Contains(preview, r => r.GetProperty("problems").EnumerateArray().Any(p => p.GetString()!.Contains("overdue")));
        Assert.Equal("Outside micrometer", preview[0].GetProperty("values").GetProperty("name").GetString());
        Assert.Equal(0, await DbAsync(db => db.Equipment.CountAsync(e => e.Number.StartsWith(code)))); // a dry run writes nothing

        // 10. Import.
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal(2, job.GetProperty("importedRows").GetInt32());

        // 11. Verify calibration records.
        var eq = await DbAsync(db => db.Equipment.AsNoTracking().Where(e => e.Number.StartsWith(code)).OrderBy(e => e.Number).ToListAsync());
        Assert.Equal([$"{code}-G1", $"{code}-G2"], eq.Select(e => e.Number));
        Assert.Equal(730, eq[1].CalibrationIntervalDays);
        Assert.Equal(DateOnly.Parse(next), eq[0].NextDueOn);
        Assert.Equal("Acme Calibration", eq[1].CalibrationProvider);

        // 12. Verify import audit: every state change audited, the created records linked to the job.
        Assert.True(await DbAsync(db => db.AuditEntries.CountAsync(a => a.EntityId == id && a.Action == "StatusChanged")) >= 6);
        Assert.Equal(2, await DbAsync(db => db.ImportedItems.CountAsync(i => i.ImportJobId == id)));

        // 13. Verify formulas remain in original controlled workbook.
        var original = await (await author.GetAsync($"/api/imports/{id}/original")).Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, original);
        using var back = new XLWorkbook(new MemoryStream(original));
        Assert.Equal("COUNTA(Equipment!A2:A20)", back.Worksheet("Summary").Cell("B3").FormulaA1);
        Assert.True(job.GetProperty("analysis").GetProperty("workbook").GetProperty("formulaTotal").GetInt32() >= 2);
    }

    /// <summary>END-TO-END TEST 3: EXCEL MASTER DOCUMENT LIST.</summary>
    [Fact]
    public async Task E2E3_excel_master_document_list_is_imported_transactionally_into_controlled_documents()
    {
        var code = "MDL" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var site = await app.CreateSiteAsync(code);
        var admin = await app.AdminAsync();
        var dept = await (await admin.PostOkAsync($"/api/organization/sites/{site}/departments", new { code = "QA" + code[^3..], name = "Quality assurance " + code })).Content.ReadFromJsonAsync<Guid>();
        var (ownerId, _) = await app.CreateUserAsync([Permissions.DocumentsView], site);
        var ownerEmail = await DbAsync(db => db.Users.Where(u => u.Id == ownerId).Select(u => u.Email!).SingleAsync());
        var (_, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.ImportsView, Permissions.ImportsApprove], site);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Master document list");
        string[] head = ["Document number", "Title", "Category", "Revision", "Department", "Owner", "Review date", "Confidentiality", "Status"];
        for (var i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        string[][] rows =
        [
            [$"{code}-QP-001", "Control of documented information", "Procedure", "D", "Quality assurance " + code, ownerEmail, "2027-06-30", "Internal", "Effective"],
            [$"{code}-QP-002", "Internal audit", "Procedure", "B", "QA" + code[^3..], ownerEmail, "2027-03-31", "Internal", "Effective"],
            [$"{code}-WI-010", "Torque wrench use", "Work instruction", "A", "Quality assurance " + code, ownerEmail, "2027-01-31", "Secret", "Effective"],
            [$"{code}-QP-001", "Control of documented information", "Procedure", "D", "Quality assurance " + code, ownerEmail, "2027-06-30", "Internal", "Effective"],
        ];
        for (var r = 0; r < rows.Length; r++) for (var i = 0; i < head.Length; i++) ws.Cell(r + 2, i + 1).Value = rows[r][i];
        using var ms = new MemoryStream(); wb.SaveAs(ms);

        // 1. Upload workbook. 2. Detect document rows.
        var id = await UploadAsync(author, site, "master document list.xlsx", ms.ToArray(), "master-list", "master-documents");
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal(4, job.GetProperty("totalRows").GetInt32());

        // 3. Map document numbers and revisions.
        var maps = job.GetProperty("mappings").EnumerateArray().ToDictionary(m => m.GetProperty("sourceName").GetString()!, m => m.GetProperty("targetField").ValueKind == JsonValueKind.String ? m.GetProperty("targetField").GetString() : null);
        Assert.Equal("number", maps["Document number"]);
        Assert.Equal("revision", maps["Revision"]);
        Assert.Equal("department", maps["Department"]);
        Assert.Equal("owner", maps["Owner"]);

        // 4. Resolve departments and owners (by name, by code and by e-mail), 5. identify duplicates, 6. validate status.
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(2, dry.GetProperty("valid").GetInt32());
        Assert.Equal(1, dry.GetProperty("inError").GetInt32());
        Assert.Equal(1, dry.GetProperty("duplicates").GetInt32());
        var preview = dry.GetProperty("preview").EnumerateArray().ToList();
        // The preview shows the file's own text; the resolved department and owner are checked on the imported records in step 8.
        Assert.Empty(preview[0].GetProperty("problems").EnumerateArray());
        Assert.Empty(preview[1].GetProperty("problems").EnumerateArray());
        Assert.Contains(preview[2].GetProperty("problems").EnumerateArray(), p => p.GetString()!.Contains("Confidentiality"));

        // 7. Import transactionally: the valid rows in one transaction, approved by someone else.
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve")).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PartiallyImported, Status(job));
        Assert.Equal(2, job.GetProperty("importedRows").GetInt32());

        // 8. Verify controlled records: drafts at the file's revision, owned and filed as the list says; "Effective" in the file is not taken over,
        //    the documents become effective only through their approval route.
        var docs = await DbAsync(db => db.Documents.AsNoTracking().Include(d => d.Revisions).Where(d => d.Number.StartsWith(code)).OrderBy(d => d.Number).ToListAsync());
        Assert.Equal([$"{code}-QP-001", $"{code}-QP-002"], docs.Select(d => d.Number));
        Assert.All(docs, d => { Assert.Equal(DocumentStatus.Draft, d.Status); Assert.Equal(dept, d.DepartmentId); Assert.Equal(ownerId, d.OwnerId); Assert.Equal(RevisionStatus.Draft, d.Revisions.Single().Status); });
        Assert.Equal("D", docs[0].Revisions.Single().Revision);

        // 9. Verify rejected-row export (spreadsheet-safe CSV with the reason).
        var rejected = await (await author.GetAsync($"/api/imports/{id}/error-rows")).Content.ReadAsStringAsync();
        Assert.Contains($"{code}-WI-010", rejected);
        Assert.Contains("Confidentiality", rejected);
        Assert.DoesNotContain($"{code}-QP-002", rejected);
    }

    /// <summary>END-TO-END TEST 4: PPAP PACKAGE.</summary>
    [Fact]
    public async Task E2E4_ppap_package_is_indexed_reviewed_approved_and_exported_with_its_relationships()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpEdit, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var (_, mgr) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpApprove], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.ApqpView], await app.CreateSiteAsync());
        var tag = Guid.NewGuid().ToString("N")[..6];
        var productId = (await (await eng.PostOkAsync("/api/master/products", new { number = "PR-" + tag, name = "Bracket " + tag, category = "Brackets", siteId = site, status = 0 })).JsonAsync()).Id();
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = "BR-" + tag, name = "Mounting bracket", partType = 0, revision = "B", unitOfMeasure = "EA", productId, siteId = site, status = 0 })).JsonAsync()).Id();
        var customerId = (await (await eng.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Northwind " + tag, siteId = site, status = 0 })).JsonAsync()).Id();

        // 1. Create an original organization-authored PPAP package.
        var ppap = (await (await eng.PostOkAsync("/api/launch/ppap", new { partId, partRevision = "B", customerId, level = 3, reason = "Initial submission", siteId = site })).JsonAsync()).Id();

        // 2. Configure required elements (the level sets the requirement of each element; the warrant is always required).
        var elements = (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal(18, elements.Count);
        Assert.Equal("Submit", elements.Single(e => e.Int("code") == 18).Str("requirement"));
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PutAsync($"/api/launch/ppap/{ppap}/elements/18", new { status = 2, linkedType = (string?)null, linkedId = (Guid?)null, notes = "n/a" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/launch/ppap/{ppap}/review")).StatusCode); // required elements still missing

        // 3. Attach relevant QMS records.
        var pf = (await (await eng.PostOkAsync("/api/apqp/pfmea", new { title = "Bracket PFMEA", siteId = site, departmentId = (Guid?)null, productId, partId, partRevision = "B", ownerId = (Guid?)null, phase = 2, productFamily = "Brackets", pfmeaId = (Guid?)null, processFlowId = (Guid?)null, supplierId = (Guid?)null, processOwnerId = (Guid?)null })).JsonAsync()).Id();
        var cp = (await (await eng.PostOkAsync("/api/apqp/plans", new { title = "Bracket control plan", siteId = site, departmentId = (Guid?)null, productId, partId, partRevision = "B", ownerId = (Guid?)null, phase = 2, productFamily = "Brackets", pfmeaId = pf, processFlowId = (Guid?)null, supplierId = (Guid?)null, processOwnerId = (Guid?)null })).JsonAsync()).Id();
        var cap = (await (await eng.PostOkAsync("/api/launch/capability", new { title = "Hole position capability", characteristic = "Hole position", siteId = site, partId, controlPlanId = cp, lsl = 9.95m, usl = 10.05m, subgroupSize = 1,
            values = new[] { 10.000m, 10.004m, 9.998m, 10.002m, 9.996m, 10.001m, 10.003m, 9.999m, 10.000m, 10.002m, 9.997m, 10.001m } })).JsonAsync()).Id();
        foreach (var e in elements.Where(e => e.Str("status") == "Missing"))
        {
            var code = e.Int("code");
            object body = code == 6 ? new { status = 1, linkedType = "Pfmea", linkedId = pf, notes = (string?)null }
                : code == 7 ? new { status = 1, linkedType = "ControlPlan", linkedId = cp, notes = (string?)null }
                : code == 11 ? new { status = 1, linkedType = "CapabilityStudy", linkedId = cap, notes = (string?)null }
                : e.Str("requirement") == "Submit" || code == 18 ? new { status = 1, linkedType = (string?)null, linkedId = (Guid?)null, notes = (string?)$"Evidence held by quality engineering ({code})" }
                : new { status = 2, linkedType = (string?)null, linkedId = (Guid?)null, notes = (string?)"Not required by the customer" };
            var r = await eng.PutAsync($"/api/launch/ppap/{ppap}/elements/{code}", body);
            Assert.True(r.StatusCode == HttpStatusCode.NoContent, $"{code}: {await r.Content.ReadAsStringAsync()}");
        }

        // 4. Generate package index.
        var index = await eng.GetElAsync($"/api/launch/ppap/{ppap}/package-index");
        Assert.Equal("BR-" + tag, index.Str("partNumber"));
        Assert.Equal("B", index.Str("partRevision"));
        Assert.Equal(0, index.Int("missing"));
        var rows = index.GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal(18, rows.Count);
        var pfRow = rows.Single(r => r.Int("code") == 6);
        Assert.Equal("Pfmea", pfRow.Str("linkedType"));
        Assert.False(string.IsNullOrEmpty(pfRow.Str("linkedNumber")));
        Assert.Equal("Bracket PFMEA", pfRow.Str("linkedTitle"));
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/launch/ppap/{ppap}/package-index")).StatusCode); // another site's package is not visible
        Assert.Equal(HttpStatusCode.Conflict, (await eng.GetAsync($"/api/launch/ppap/{ppap}/package")).StatusCode); // not reviewed yet

        // 5. Route for review: elements lock while the package is in review.
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/launch/ppap/{ppap}/review")).StatusCode);
        Assert.Equal("InReview", (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).Str("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PutAsync($"/api/launch/ppap/{ppap}/elements/9", new { status = 1, linkedType = (string?)null, linkedId = (Guid?)null, notes = "late change" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PostAsync($"/api/launch/ppap/{ppap}/submit")).StatusCode);

        // 6. Approve package (e-signed by someone other than the submitter).
        var number = (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).GetProperty("submission").Str("number");
        Assert.Equal(HttpStatusCode.OK, (await mgr.SignAsync(await mgr.PendingApprovalIdAsync(number))).StatusCode);
        Assert.Equal("ReviewApproved", (await eng.GetElAsync($"/api/launch/ppap/{ppap}")).Str("status"));
        var review = (await eng.GetElAsync($"/api/launch/ppap/{ppap}/package-index")).GetProperty("review");
        Assert.Equal("Approved", review.Str("status"));
        Assert.Single(review.GetProperty("signatures").EnumerateArray());

        // 7. Export package.
        var resp = await eng.GetAsync($"/api/launch/ppap/{ppap}/package");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/zip", resp.Content.Headers.ContentType?.MediaType);
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(["package-index.csv", "package-index.json", "relationships.csv"], zip.Entries.Select(e => e.FullName).Order());
        string Entry(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8); return r.ReadToEnd(); }
        var json = JsonDocument.Parse(Entry("package-index.json")).RootElement;
        Assert.Equal(number, json.Str("number"));
        var csv = Entry("package-index.csv");
        Assert.Equal(19, csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);

        // 8. Preserve source relationships: each linked record is listed with its number, and the link rows are kept in the database.
        var rel = Entry("relationships.csv");
        foreach (var t in new[] { "Pfmea", "ControlPlan", "CapabilityStudy" }) Assert.Contains(t, rel);
        Assert.Contains(pfRow.Str("linkedNumber")!, rel);
        Assert.Contains("Pfmea", (await eng.GetElAsync($"/api/records/Ppap/{ppap}/links")).ToString());

        // 9. Confirm no proprietary template content is embedded: the package carries only the organization's own records and text.
        var all = Entry("package-index.json") + csv + rel;
        foreach (var banned in new[] { "AIAG", "Production Part Approval Process Manual", "Copyright", "©" }) Assert.DoesNotContain(banned, all, StringComparison.OrdinalIgnoreCase);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == ppap && a.Action == "Exported"));
        }

        // the reviewed package then goes to the customer as before
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/launch/ppap/{ppap}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/launch/ppap/{ppap}/customer-decision", new { decision = 2, customerName = "J. Buyer", decidedOn = TestDates.Today, comments = "PSW signed", password = Pw })).StatusCode);
    }

    /// <summary>END-TO-END TEST 5: FORMULA PRESERVATION.</summary>
    [Fact]
    public async Task E2E5_formulas_are_preserved_classified_converted_and_protected_through_the_controlled_workbook()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        byte[] bytes;
        using (var wb = new XLWorkbook(ExcelConversionTests.InspectionWorkbook()))
        {
            var ws = wb.Worksheet("Inspection");
            ws.Cell("A17").Value = "Lot in words:"; ws.Cell("C17").FormulaA1 = "BAHTTEXT(B4)";
            using var ms = new MemoryStream(); wb.SaveAs(ms); bytes = ms.ToArray();
        }

        // 1. Upload workbook containing formulas.
        var id = await UploadAsync(author, site, "Receiving inspection.xlsx", bytes, "template");
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.Equal((int)ImportJobStatus.PreviewReady, Status(job));
        var wbInfo = job.GetProperty("analysis").GetProperty("workbook");

        // 2. Detect formulas.
        var formulas = wbInfo.GetProperty("formulas").EnumerateArray().ToList();
        Assert.True(wbInfo.GetProperty("formulaTotal").GetInt32() >= 19);
        Assert.Contains(formulas, f => f.GetProperty("formula").GetString()!.Contains("IF(B4>500"));

        // 3. Classify functions.
        var sample = formulas.First(f => f.GetProperty("formula").GetString()!.Contains("IF(B4>500"));
        Assert.Contains("IF", sample.GetProperty("functions").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("server", sample.GetProperty("classes").EnumerateArray().Select(x => x.GetString()));
        Assert.True(wbInfo.GetProperty("formulaClasses").EnumerateObject().Any());

        // 4. Preserve original formulas: the stored original is the uploaded file with its formulas.
        var hash = job.GetProperty("fileHash").GetString()!;
        Assert.True(await DbAsync(db => db.Attachments.AnyAsync(a => a.OwnerId == id && a.Category == "Import original" && a.IsLocked && a.Sha256 == hash)));

        // 5. Identify unsupported formulas.
        var unsupported = formulas.Single(f => f.GetProperty("formula").GetString()!.Contains("BAHTTEXT"));
        Assert.Contains("unsupported", unsupported.GetProperty("classes").EnumerateArray().Select(x => x.GetString()));

        // 6. Display warnings.
        Assert.Contains(Issues(job), i => i.GetProperty("issueCode").GetString() == "FORMULA" && i.GetProperty("description").GetString()!.Contains("BAHTTEXT"));

        // 7. Convert supported cells into live calculated fields.
        var def = FormDefinitions.Parse(job.GetProperty("analysis").GetProperty("conversion").GetProperty("definition").GetRawText());
        Assert.Equal("IF(lot_qty>500,32,IF(lot_qty>90,13,5))", def.Fields.Single(f => f.Key == "sample_size").Formula!.Replace(" ", ""));
        Assert.Equal("calc", def.Fields.Single(f => f.Key == "sample_size").Type);
        Assert.Equal(["Pass", "Fail", "Hold"], def.Fields.Single(f => f.Key == "disposition").Options);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/detections", new { items = Array.Empty<object>(), confirmAll = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.PostAsync($"/api/imports/{id}/validate")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = true })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        var docId = Guid.Parse(job.GetProperty("summary").GetProperty("documentId").GetString()!);
        await PublishTemplateAsync(author, approver, docId);

        // 8. Reimport controlled workbook: the protected offline copy comes back as a recalculated record.
        // Each offline copy is issued once and accepted once, so every submission below starts from a newly issued copy.
        async Task<byte[]> IssueAsync() => await (await author.GetAsync($"/api/forms/templates/{docId}/workbook")).Content.ReadAsByteArrayAsync();
        var exported = await IssueAsync();
        (int Row, int Col) Find(IXLWorksheet f, string label) { var c = f.CellsUsed().First(x => x.GetString().TrimEnd(' ', '*').Equals(label, StringComparison.OrdinalIgnoreCase)); return (c.Address.RowNumber, c.Address.ColumnNumber + 1); }
        byte[] Fill(Action<IXLWorksheet> change)
        {
            using var wb = new XLWorkbook(new MemoryStream(exported));
            change(wb.Worksheet("Form"));
            using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
        }
        async Task<HttpResponseMessage> SubmitAsync(byte[] file)
        {
            var form = new MultipartFormDataContent();
            var f = new ByteArrayContent(file); f.Headers.ContentType = new MediaTypeHeaderValue(Xlsx);
            form.Add(f, "file", "completed.xlsx");
            form.Add(new StringContent(site.ToString()), "siteId");
            return await author.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/forms/templates/{docId}/workbook") { Content = form });
        }
        var lotLabel = def.Fields.Single(f => f.Key == "lot_qty").Label;
        var dispLabel = def.Fields.Single(f => f.Key == "disposition").Label;
        var r = await SubmitAsync(Fill(f => { var (row, col) = Find(f, lotLabel); f.Cell(row, col).Value = 600; var (dr, dc) = Find(f, dispLabel); f.Cell(dr, dc).Value = "Pass"; }));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var recId = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recordId").GetGuid();
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(await DbAsync(db => db.FormRecords.Where(x => x.Id == recId).Select(x => x.ValuesJson).SingleAsync()))!;
        Assert.Equal("32", values["sample_size"]);

        // 9. Validate input cells: a value outside the dropdown list is refused.
        exported = await IssueAsync();
        var bad = await SubmitAsync(Fill(f => { var (row, col) = Find(f, lotLabel); f.Cell(row, col).Value = 50; var (dr, dc) = Find(f, dispLabel); f.Cell(dr, dc).Value = "Maybe"; }));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // 10. Prevent altered protected structure.
        exported = await IssueAsync();
        var altered = await SubmitAsync(Fill(f => { var (row, col) = Find(f, lotLabel); f.Cell(row, col - 1).Value = "Batch size"; }));
        Assert.Equal(HttpStatusCode.BadRequest, altered.StatusCode);
        Assert.Contains("protected parts", await altered.Content.ReadAsStringAsync());
    }

    static byte[] OversizedPackage()
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            using (var w = new StreamWriter(z.CreateEntry("[Content_Types].xml").Open())) w.Write("<Types/>");
            using (var w = new StreamWriter(z.CreateEntry("xl/worksheets/sheet1.xml").Open())) w.Write(new string('0', 1000));
        }
        // the central directory declares the sheet as 3.5 GB once expanded (a compression bomb); nothing is ever expanded to find out
        var b = ms.ToArray();
        for (var i = b.Length - 22; i >= 0; i--)
            if (b[i] == 0x50 && b[i + 1] == 0x4B && b[i + 2] == 0x01 && b[i + 3] == 0x02)
            {
                var nameLen = BitConverter.ToUInt16(b, i + 28);
                var name = Encoding.ASCII.GetString(b, i + 46, nameLen);
                if (name.EndsWith("sheet1.xml")) BitConverter.GetBytes(0xD0000000u).CopyTo(b, i + 24);
            }
        return b;
    }

    /// <summary>END-TO-END TEST 6: SECURITY.</summary>
    [Fact]
    public async Task E2E6_unsafe_files_unauthorized_access_and_cross_site_imports_are_refused_or_quarantined_and_logged()
    {
        var site = await app.CreateSiteAsync();
        var other = await app.CreateSiteAsync();
        var (authorId, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (viewerId, viewer) = await app.CreateUserAsync([Permissions.ImportsView], site);
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);

        // 1. Upload malformed Office package.
        var malformed = await UploadAsync(author, site, "broken.xlsx", [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8], "template");
        var job = await author.GetElAsync($"/api/imports/{malformed}");
        Assert.Equal((int)ImportJobStatus.SecurityScanFailed, Status(job));
        Assert.Equal("SECURITY", Issues(job)[0].GetProperty("issueCode").GetString());

        // 2. Upload suspicious macro workbook: macros in an .xlsx are refused; an .xlsm is accepted with its macros never run.
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true)) { z.CreateEntry("[Content_Types].xml"); z.CreateEntry("xl/vbaProject.bin"); }
        var hidden = await UploadAsync(author, site, "innocent.xlsx", ms.ToArray(), "template");
        job = await author.GetElAsync($"/api/imports/{hidden}");
        Assert.Equal((int)ImportJobStatus.SecurityScanFailed, Status(job));
        Assert.Contains("macros", Issues(job)[0].GetProperty("description").GetString());
        var fx = ImportFixtures.All.Single(f => f.FileName == "macro-workbook.xlsm");
        var xlsm = await UploadAsync(author, site, fx.FileName, fx.Build(), "template", type: fx.ContentType);
        job = await author.GetElAsync($"/api/imports/{xlsm}");
        Assert.Contains(Issues(job), i => i.GetProperty("issueCode").GetString() == "WORKBOOK_SECURITY" && i.GetProperty("description").GetString()!.Contains("never run"));

        // 3. Upload file with external references: kept as text, never followed.
        var ext = ImportFixtures.All.Single(f => f.FileName == "external-links.xlsx");
        var extId = await UploadAsync(author, site, ext.FileName, ext.Build(), "template", type: ext.ContentType);
        job = await author.GetElAsync($"/api/imports/{extId}");
        Assert.Contains(Issues(job), i => i.GetProperty("issueCode").GetString() == "WORKBOOK_SECURITY" && i.GetProperty("description").GetString()!.Contains("never followed"));

        // 4. Upload oversized archive.
        var bomb = await UploadAsync(author, site, "bomb.xlsx", OversizedPackage(), "template");
        job = await author.GetElAsync($"/api/imports/{bomb}");
        Assert.Equal((int)ImportJobStatus.SecurityScanFailed, Status(job));
        Assert.Contains("1 GB", Issues(job)[0].GetProperty("description").GetString());

        // 5. Attempt unauthorized file access: viewing an import does not allow downloading its native file.
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/imports/{extId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/imports/{extId}/original")).StatusCode);
        var (_, stranger) = await app.CreateUserAsync([Permissions.ImportsView, Permissions.ImportsDownloadOriginal], other);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/imports/{extId}/original")).StatusCode);

        // 6. Attempt cross-site import.
        var cross = await UploadRawAsync(author, other, "cross-site.csv", "Part,Qty\nA,1\n"u8.ToArray(), "records", "form-records", "text/csv");
        Assert.Equal(HttpStatusCode.Forbidden, cross.StatusCode);

        // 7. Confirm all are rejected or safely quarantined: refused files never get past the scan, active content is not run, nothing was imported.
        Assert.Equal(0, await DbAsync(db => db.ImportJobs.CountAsync(j => j.SiteId == other)));
        foreach (var refused in new[] { malformed, hidden, bomb })
        {
            Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync($"/api/imports/{refused}/validate")).StatusCode);
            Assert.Equal(0, await DbAsync(db => db.ImportedItems.CountAsync(i => i.ImportJobId == refused)));
        }

        // 8. Confirm security logging.
        var events = await DbAsync(db => db.SecurityEvents.AsNoTracking().Where(e => e.OccurredAt >= since && (e.UserId == authorId || e.UserId == viewerId)).ToListAsync());
        Assert.Equal(3, events.Count(e => e.EventType == "ImportFileRejected" && !e.Succeeded));
        Assert.Contains(events, e => e.EventType == "ImportFileRejected" && e.Detail!.Contains("bomb.xlsx"));
        Assert.Equal(2, events.Count(e => e.EventType == "ImportActiveContent"));
        Assert.Contains(events, e => e.EventType == "ImportAccessDenied" && e.UserId == viewerId);
        Assert.Contains(events, e => e.EventType == "ImportAccessDenied" && e.UserId == authorId && e.Detail!.Contains(other.ToString()));
    }
}
