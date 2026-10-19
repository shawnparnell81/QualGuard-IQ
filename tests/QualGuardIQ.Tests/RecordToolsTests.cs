using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Delete with a reason and restore, record and list export (PDF, CSV, Excel, Word), NCR, CAPA and training log imports, and settings.</summary>
[Collection("api")]
public sealed class RecordToolsTests(TestApp app)
{
    static readonly string[] NcrPeople = [Permissions.NcrView, Permissions.NcrEdit];

    async Task<Guid> NcrAsync(ApiClient c, Guid site, string title) =>
        (await (await c.PostOkAsync("/api/ncrs", new { title, description = "Edge burr on 4 of 20 brackets", source = "In-process", siteId = site, detectedOn = TestDates.Today, severity = 1 })).JsonAsync()).Id();

    static async Task<string> Body(HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    [Fact]
    public async Task Deleting_needs_the_module_permission_and_a_reason_hides_the_record_everywhere_and_restore_brings_it_back()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(NcrPeople, site);
        var (_, deleter) = await app.CreateUserAsync([.. NcrPeople, Permissions.NcrDelete], site);
        var title = "Burr " + Guid.NewGuid().ToString("N")[..8];
        var id = await NcrAsync(author, site, title);

        var cap = await author.GetElAsync($"/api/records/ncr/{id}/capabilities");
        Assert.True(cap.GetProperty("delete").GetProperty("deletable").GetBoolean());
        Assert.False(cap.GetProperty("delete").GetProperty("canDelete").GetBoolean());
        Assert.Equal(Permissions.NcrDelete, cap.GetProperty("delete").GetProperty("deletePermission").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "Created by mistake" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await deleter.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await deleter.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "dup" })).StatusCode);
        // the page finds the record from its address
        var located = await deleter.GetElAsync($"/api/records/locate/{id}?path=/ncr/{id}");
        Assert.Equal("ncr", located.GetProperty("type").GetString(), ignoreCase: true);
        Assert.True(located.GetProperty("delete").GetProperty("canDelete").GetBoolean());

        var del = await deleter.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "Duplicate of an NCR raised by the night shift" });
        Assert.True(del.IsSuccessStatusCode, await Body(del));
        var entry = (await del.JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NotFound, (await deleter.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "Again, by mistake" })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/ncrs/{id}")).StatusCode);
        Assert.DoesNotContain(id.ToString(), await Body(await author.GetAsync($"/api/ncrs?q={title}")));
        Assert.DoesNotContain(id.ToString(), await Body(await author.GetAsync($"/api/search?q={title}")));

        var admin = await app.AdminAsync();
        var trail = await Body(await admin.GetAsync("/api/audit-trail?page=1&pageSize=50&entityType=Ncr"));
        Assert.Contains("Reason: Duplicate of an NCR raised by the night shift", trail);
        Assert.Contains(title, trail);

        // the deleted list shows it to people who may delete NCRs, not to others
        Assert.Contains(title, await Body(await deleter.GetAsync("/api/deleted-records")));
        Assert.DoesNotContain(title, await Body(await author.GetAsync("/api/deleted-records")));
        Assert.Equal(HttpStatusCode.NotFound, (await author.PostAsync($"/api/deleted-records/{entry}/restore", new { reason = "Needed after all" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await deleter.PostAsync($"/api/deleted-records/{entry}/restore", new { reason = "" })).StatusCode);
        Assert.True((await deleter.PostAsync($"/api/deleted-records/{entry}/restore", new { reason = "Not a duplicate after all" })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await deleter.PostAsync($"/api/deleted-records/{entry}/restore", new { reason = "Not a duplicate after all" })).StatusCode);

        Assert.Equal(title, (await author.GetElAsync($"/api/ncrs/{id}")).Str("title"));
        Assert.Contains(id.ToString(), await Body(await author.GetAsync($"/api/search?q={title}")));
        trail = await Body(await admin.GetAsync("/api/audit-trail?page=1&pageSize=50&entityType=Ncr"));
        Assert.Contains("Not a duplicate after all", trail);
        var history = await deleter.GetElAsync("/api/deleted-records?includeRestored=true");
        var row = history.EnumerateArray().Single(x => x.Id("recordId") == id);
        Assert.Equal("Not a duplicate after all", row.Str("restoreReason"));
        // and it can be deleted again later
        Assert.True((await deleter.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "Raised against the wrong part" })).IsSuccessStatusCode);
    }

    [Fact]
    public async Task Closed_records_need_the_delete_locked_permission()
    {
        var site = await app.CreateSiteAsync();
        var (_, deleter) = await app.CreateUserAsync([.. NcrPeople, Permissions.NcrDelete], site);
        var (_, senior) = await app.CreateUserAsync([.. NcrPeople, Permissions.NcrDelete, Permissions.RecordsDeleteLocked], site);
        var id = await NcrAsync(deleter, site, "Closed NCR to remove");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Ncrs.Where(n => n.Id == id).ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, NcrStatus.Closed).SetProperty(n => n.ClosureNotes, "Closed"));
        }
        var check = (await deleter.GetElAsync($"/api/records/ncr/{id}/capabilities")).GetProperty("delete");
        Assert.True(check.GetProperty("locked").GetBoolean());
        Assert.False(check.GetProperty("canDelete").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await deleter.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "Entered twice by mistake" })).StatusCode);
        var r = await senior.PostAsync($"/api/records/ncr/{id}/delete", new { reason = "Entered twice by mistake" });
        Assert.True(r.IsSuccessStatusCode, await Body(r));
        var deleted = await r.JsonAsync();
        Assert.True(deleted.GetProperty("wasLocked").GetBoolean());
        // restoring a record that was locked also needs the higher permission
        var entry = deleted.Id();
        Assert.Equal(HttpStatusCode.Forbidden, (await deleter.PostAsync($"/api/deleted-records/{entry}/restore", new { reason = "Should stay on file" })).StatusCode);
        Assert.True((await senior.PostAsync($"/api/deleted-records/{entry}/restore", new { reason = "Should stay on file" })).IsSuccessStatusCode);
    }

    [Fact]
    public void Every_deletable_record_type_has_a_delete_permission_and_is_hidden_by_the_database_filter()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var providers = scope.ServiceProvider.GetServices<IRecordTypeProvider>().Where(p => p.EntityType is { } t && typeof(ISoftDeletable).IsAssignableFrom(t)).ToList();
        Assert.NotEmpty(providers);
        foreach (var p in providers)
        {
            var et = db.Model.FindEntityType(p.EntityType!) ?? db.Model.GetEntityTypes().First(e => p.EntityType!.IsAssignableFrom(e.ClrType));
            var root = et.GetRootType();
            Assert.True(root.GetDeclaredQueryFilters().Any(), $"{p.Type}: {root.ClrType.Name} has no deleted-record filter");
        }
        // each module's edit permission has a matching delete permission; nothing is tied to a role name
        foreach (var edit in Permissions.All.Where(x => x.EndsWith(".edit", StringComparison.Ordinal)))
        {
            var d = Permissions.DeleteFor(edit);
            if (d is not null) Assert.Contains(d, Permissions.All);
        }
        Assert.Equal(Permissions.NcrDelete, Permissions.DeleteFor(Permissions.NcrEdit));
        Assert.Contains(Permissions.RecordsDeleteLocked, Permissions.All);
    }

    [Fact]
    public async Task A_record_exports_as_PDF_CSV_Excel_and_Word_and_each_export_is_audited()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(NcrPeople, site);
        var (_, outsider) = await app.CreateUserAsync(NcrPeople, await app.CreateSiteAsync());
        var title = "Export " + Guid.NewGuid().ToString("N")[..8];
        var id = await NcrAsync(author, site, title);

        var pdf = await author.GetAsync($"/api/records/ncr/{id}/export?format=pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.EndsWith(".pdf", pdf.Content.Headers.ContentDisposition?.FileNameStar ?? pdf.Content.Headers.ContentDisposition?.FileName?.Trim('"'));

        var csv = Encoding.UTF8.GetString(await (await author.GetAsync($"/api/records/ncr/{id}/export?format=csv")).Content.ReadAsByteArrayAsync());
        Assert.Contains(title, csv);
        Assert.Contains("In-process", csv);

        using (var xs = new MemoryStream(await (await author.GetAsync($"/api/records/ncr/{id}/export?format=xlsx")).Content.ReadAsByteArrayAsync()))
        using (var wb = new XLWorkbook(xs))
            Assert.Contains(wb.Worksheets.SelectMany(w => w.CellsUsed()), c => c.GetString().Contains(title));

        using (var ds = new MemoryStream(await (await author.GetAsync($"/api/records/ncr/{id}/export?format=docx")).Content.ReadAsByteArrayAsync()))
        using (var zip = new ZipArchive(ds))
        {
            using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            var xml = await reader.ReadToEndAsync();
            Assert.Contains(title, xml);
            Assert.Contains("In-process", xml); // the value beside each label, not only the labels
            Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await author.GetAsync($"/api/records/ncr/{id}/export?format=exe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/records/ncr/{id}/export?format=pdf")).StatusCode);

        var trail = await Body(await (await app.AdminAsync()).GetAsync("/api/audit-trail?page=1&pageSize=50&entityType=Ncr"));
        Assert.Contains("Printed", trail);
        Assert.Contains("Exported", trail);
    }

    [Fact]
    public async Task A_list_exports_as_shown_with_spreadsheet_formulas_neutralised_and_size_limits()
    {
        var (_, user) = await app.CreateUserAsync([Permissions.NcrView]);
        object Req(string format, IEnumerable<string>? cols = null) => new
        {
            title = "Open NCRs", format, columns = cols ?? ["NCR", "Title", "Status"],
            rows = new[] { new[] { "NCR-0001", "=HYPERLINK(\"http://x\")", "Open" }, new[] { "NCR-0002", "Burr, on edge \"B\"", "Closed" } },
            filters = "Status: Open",
        };
        var csv = Encoding.UTF8.GetString(await (await user.PostOkAsync("/api/export/table", Req("csv"))).Content.ReadAsByteArrayAsync());
        Assert.StartsWith("\uFEFFNCR,Title,Status", csv);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.Contains("\"Burr, on edge \"\"B\"\"\"", csv);

        using (var xs = new MemoryStream(await (await user.PostOkAsync("/api/export/table", Req("xlsx"))).Content.ReadAsByteArrayAsync()))
        using (var wb = new XLWorkbook(xs))
        {
            var cells = wb.Worksheets.SelectMany(w => w.CellsUsed()).ToList();
            Assert.Contains(cells, c => c.GetString() == "NCR-0002");
            Assert.DoesNotContain(cells, c => c.HasFormula);
        }
        var docx = await (await user.PostOkAsync("/api/export/table", Req("docx"))).Content.ReadAsByteArrayAsync();
        Assert.Equal("PK", Encoding.ASCII.GetString(docx, 0, 2));
        var pdf = await (await user.PostOkAsync("/api/export/table", Req("pdf", ["A", "B", "C", "D", "E", "F", "G"]))).Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));

        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/export/table", Req("csv", Enumerable.Range(0, 61).Select(i => $"C{i}")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsync("/api/export/table", Req("rtf"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await app.AnonymousAsync()).PostAsync("/api/export/table", Req("csv"))).StatusCode);
    }

    static async Task<Guid> UploadAsync(ApiClient c, Guid site, string name, byte[] content, string target, string contentType)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content); file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", name);
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent("records"), "mode");
        form.Add(new StringContent(target), "targetModule");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    static async Task<JsonElement> ImportAsync(ApiClient author, ApiClient approver, Guid id, bool skipInvalid = false)
    {
        var sub = await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = skipInvalid });
        Assert.True(sub.StatusCode == HttpStatusCode.NoContent, await sub.Content.ReadAsStringAsync());
        await approver.PostAsync($"/api/imports/{id}/approve");
        return await author.GetElAsync($"/api/imports/{id}");
    }

    [Fact]
    public async Task An_NCR_log_imports_from_CSV_with_errors_per_row_and_all_rows_or_none()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([.. NcrPeople, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.NcrView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var csv = "NCR number,Title,Description,Source,Severity,Status,Closure notes\n"
            + $",Scratch on cover {tag},Cosmetic scratch,Receiving,Minor,Open,\n"
            + $"LEG-{tag}-7,Missing label {tag},No label,Final inspection,Major,Closed,Relabelled and released\n"
            + $",Wrong torque {tag},Over-torqued,Assembly,Severe,DispositionPending,\n";
        var id = await UploadAsync(author, site, "ncr log.csv", Encoding.UTF8.GetBytes(csv), "log-ncrs", "text/csv");
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(2, dry.Int("valid"));
        Assert.Equal(1, dry.Int("inError"));
        var problems = dry.GetProperty("preview")[2].GetProperty("problems").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains(problems, p => p!.Contains("Severe"));

        // all or nothing: without "only the rows without problems" the file can't go forward and nothing is created
        var refused = await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Some rows have problems", await Body(refused));
        Assert.DoesNotContain($"Scratch on cover {tag}", await Body(await author.GetAsync($"/api/ncrs?q={tag}")));

        var fixedId = await UploadAsync(author, site, "ncr log.csv", Encoding.UTF8.GetBytes(csv.Replace("Severe,DispositionPending", "Major,Open")), "log-ncrs", "text/csv");
        Assert.Equal(3, (await (await author.PostOkAsync($"/api/imports/{fixedId}/validate")).JsonAsync()).Int("valid"));
        var job = await ImportAsync(author, approver, fixedId);
        Assert.Equal((int)ImportJobStatus.Imported, job.Int("status"));
        var list = await Body(await author.GetAsync($"/api/ncrs?q={tag}"));
        Assert.Contains($"LEG-{tag}-7", list);
        Assert.Contains($"Wrong torque {tag}", list);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var closed = await db.Ncrs.AsNoTracking().SingleAsync(n => n.Number == $"LEG-{tag}-7");
        Assert.Equal(NcrStatus.Closed, closed.Status);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == closed.Id));
    }

    [Fact]
    public async Task A_CAPA_log_imports_from_Excel_and_training_records_from_CSV()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.CapaView, Permissions.CapaEdit, Permissions.TrainingView, Permissions.TrainingEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.CapaView, Permissions.TrainingView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        byte[] book;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("CAPAs");
            string[] head = ["Title", "Problem statement", "Source", "Type", "Risk"];
            for (var i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
            ws.Cell(2, 1).Value = $"Torque drift {tag}"; ws.Cell(2, 2).Value = "Readings drift after 4 hours"; ws.Cell(2, 3).Value = "NCR trend"; ws.Cell(2, 4).Value = "Corrective"; ws.Cell(2, 5).Value = "Major";
            ws.Cell(3, 1).Value = $"Label misprints {tag}"; ws.Cell(3, 2).Value = "Smudged labels"; ws.Cell(3, 3).Value = "Audit"; ws.Cell(3, 4).Value = "Preventive"; ws.Cell(3, 5).Value = "Minor";
            using var ms = new MemoryStream(); wb.SaveAs(ms); book = ms.ToArray();
        }
        var id = await UploadAsync(author, site, "capa log.xlsx", book, "log-capas", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        Assert.Equal(2, (await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync()).Int("valid"));
        Assert.Equal((int)ImportJobStatus.Imported, (await ImportAsync(author, approver, id)).Int("status"));
        Assert.Contains($"Torque drift {tag}", await Body(await author.GetAsync($"/api/capas?q={tag}")));

        var course = (await (await author.PostOkAsync("/api/training/courses", new { title = $"Torque basics {tag}", description = "Torque tools", siteId = site, departmentId = (Guid?)null, documentId = (Guid?)null, retrainOnRevision = false, validityMonths = 24, dueDays = 14 })).JsonAsync()).Id();
        var csv = $"Person,Course number or title,Completed on\n{TestApp.AdminEmail},Torque basics {tag},{TestDates.Today.AddDays(-3):yyyy-MM-dd}\nnobody@test.local,Torque basics {tag},\n";
        var tid = await UploadAsync(author, site, "training.csv", Encoding.UTF8.GetBytes(csv), "log-training", "text/csv");
        var dry = await (await author.PostOkAsync($"/api/imports/{tid}/validate")).JsonAsync();
        Assert.Equal(1, dry.Int("valid"));
        Assert.Equal(1, dry.Int("inError"));
        Assert.Equal((int)ImportJobStatus.PartiallyImported, (await ImportAsync(author, approver, tid, skipInvalid: true)).Int("status"));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = await db.TrainingAssignments.AsNoTracking().SingleAsync(x => x.CourseId == course);
        Assert.Equal(AssignmentStatus.Completed, a.Status);
    }

    [Fact]
    public async Task Organization_settings_numbering_logo_and_system_status_are_for_administrators()
    {
        var admin = await app.AdminAsync();
        var (_, user) = await app.CreateUserAsync([Permissions.NcrView]);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/settings/system")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/settings/numbering")).StatusCode);
        var system = await admin.GetElAsync("/api/settings/system");
        Assert.True(system.GetProperty("signIn").GetProperty("sessionAbsoluteHours").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Object, system.GetProperty("scanning").ValueKind);

        // numbering: the next number follows the new format
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(NcrPeople, site);
        await NcrAsync(author, site, "Make sure the NCR sequence exists");
        var ncr = (await admin.GetElAsync("/api/settings/numbering")).EnumerateArray().First(x => x.Str("recordType").Equals("ncr", StringComparison.OrdinalIgnoreCase));
        var before = new { prefix = ncr.Str("prefix"), padding = ncr.Int("padding"), resetYearly = ncr.GetProperty("resetYearly").GetBoolean() };
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/settings/numbering/{ncr.Str("recordType")}", new { prefix = "<b>", padding = 4, resetYearly = false })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/settings/numbering/{ncr.Str("recordType")}", new { prefix = "NC-", padding = 0, resetYearly = false })).StatusCode);
            var saved = await (await admin.PutAsync($"/api/settings/numbering/{ncr.Str("recordType")}", new { prefix = "NC-", padding = 6, resetYearly = true })).JsonAsync();
            Assert.Matches(@"^NC-\d{4}-\d{6}$", saved.Str("example"));
            var id = await NcrAsync(author, site, "Numbered in the new format");
            Assert.Matches(@"^NC-\d{4}-\d{6}$", (await author.GetElAsync($"/api/ncrs/{id}")).Str("number"));
        }
        finally { await admin.PutAsync($"/api/settings/numbering/{ncr.Str("recordType")}", before); }

        // logo: PNG or JPEG by content, shown to anyone (sign-in page), removable
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGNgYGD4z8DAwMDAwMDAAAANAAH/BqJ+qwAAAABJRU5ErkJggg==");
        async Task<HttpResponseMessage> Upload(ApiClient c, byte[] b, string name)
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent(b), "file", name } };
            return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/settings/logo") { Content = form });
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(user, png, "logo.png")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(admin, Encoding.UTF8.GetBytes("<svg onload=alert(1)>"), "logo.png")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Upload(admin, png, "logo.png")).StatusCode);
        var shown = await (await app.AnonymousAsync()).GetAsync("/api/settings/logo");
        Assert.Equal("image/png", shown.Content.Headers.ContentType?.MediaType);
        Assert.True((await admin.GetElAsync("/api/organization/branding")).GetProperty("hasLogo").GetBoolean());
        var pdf = await (await admin.PostOkAsync("/api/export/table", new { title = "Logo check", format = "pdf", columns = new[] { "A" }, rows = new[] { new[] { "1" } } })).Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/settings/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await app.AnonymousAsync()).GetAsync("/api/settings/logo")).StatusCode);

        // date format and the e-mail switch
        var s = await admin.GetElAsync("/api/organization/settings");
        var body = JsonSerializer.Deserialize<Dictionary<string, object?>>(s.GetRawText())!;
        body["dateFormat"] = "nonsense";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/organization/settings", body)).StatusCode);
        body["dateFormat"] = "dd-MMM-yyyy";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/organization/settings", body)).StatusCode);
        Assert.Equal("dd-MMM-yyyy", (await admin.GetElAsync("/api/organization/settings")).Str("dateFormat"));
        var reset = JsonSerializer.Deserialize<Dictionary<string, object?>>((await admin.GetElAsync("/api/organization/settings")).GetRawText())!;
        reset["dateFormat"] = "auto";
        await admin.PutAsync("/api/organization/settings", reset);
    }

    [Fact]
    public async Task Personal_preferences_are_saved_and_turn_off_e_mail_copies_of_notifications()
    {
        var (meId, me) = await app.CreateUserAsync([Permissions.NcrView]);
        var (otherId, _) = await app.CreateUserAsync([Permissions.NcrView]);
        var p = await me.GetElAsync("/api/account/preferences");
        Assert.True(p.GetProperty("emailNotifications").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await me.PutAsync("/api/account/preferences", new { emailNotifications = true, dateFormat = "yy", theme = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await me.PutAsync("/api/account/preferences", new { emailNotifications = true, dateFormat = (string?)null, theme = "neon" })).StatusCode);
        var saved = await (await me.PutAsync("/api/account/preferences", new { emailNotifications = false, dateFormat = "dd/MM/yyyy", theme = "dark" })).JsonAsync();
        Assert.Equal("dd/MM/yyyy", saved.Str("effectiveDateFormat"));
        Assert.Equal("dark", saved.Str("theme"));

        using var scope = app.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();
        var title = "Pref check " + Guid.NewGuid().ToString("N")[..6];
        await notifier.NotifyAsync([meId, otherId], title, null, null);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sent = await db.Notifications.AsNoTracking().Where(n => n.Title == title).ToDictionaryAsync(n => n.UserId, n => n.EmailRequested);
        Assert.False(sent[meId]); // the in-app notification is still there, without the e-mail copy
        Assert.True(sent[otherId]);
    }
}
