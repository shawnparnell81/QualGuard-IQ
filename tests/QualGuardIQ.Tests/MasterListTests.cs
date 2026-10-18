using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Governed master lists (TECH 12) and master list migration (Excel mode D): rows become the module's own records, with natural keys,
/// duplicate prevention, update with restore on rollback, governed states kept, export, saved views and access by the module's permissions.</summary>
[Collection("api")]
public sealed class MasterListTests(TestApp app)
{
    static async Task<Guid> UploadAsync(ApiClient c, Guid site, string name, byte[] content, string target, string contentType = "text/csv")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content); file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", name);
        form.Add(new StringContent(site.ToString()), "siteId");
        form.Add(new StringContent("master-list"), "mode");
        form.Add(new StringContent(target), "targetModule");
        var r = await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    static async Task ApproveAsync(ApiClient author, ApiClient approver, Guid id)
    {
        var sub = await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = true });
        Assert.True(sub.StatusCode == HttpStatusCode.NoContent, await sub.Content.ReadAsStringAsync());
        var r = await approver.PostAsync($"/api/imports/{id}/approve");
        Assert.True(r.StatusCode == HttpStatusCode.NoContent, await r.Content.ReadAsStringAsync());
        var job = await author.GetElAsync($"/api/imports/{id}");
        Assert.True(job.GetProperty("status").GetInt32() is (int)ImportJobStatus.Imported or (int)ImportJobStatus.PartiallyImported or (int)ImportJobStatus.CompletedWithWarnings, job.ToString());
    }

    [Fact]
    public async Task A_calibration_master_list_migrates_into_equipment_and_an_update_import_is_undone_by_rollback()
    {
        var code = "ML" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var site = await app.CreateSiteAsync(code);
        var (_, author) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.CalibrationEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.ImportsView, Permissions.ImportsApprove], site);
        var next = DateOnly.FromDateTime(DateTime.Today).AddDays(90).ToString("yyyy-MM-dd");
        var past = DateOnly.FromDateTime(DateTime.Today).AddDays(-10).ToString("yyyy-MM-dd");
        var csv = "Equipment ID,Asset ID,Equipment name,Type,Manufacturer,Serial number,Range,Resolution,Accuracy,Acceptance criteria,Frequency (days),Last calibration,Next due date,Provider,Status,Limited use,Limited-use restriction\n"
            + $"{code}-CAL-1,A-100,Digital caliper,Caliper,Mitutoyo,SN-1,0-150 mm,0.01 mm,±0.02 mm,Within ±0.02 mm at 5 points,365,2026-01-15,{next},Internal lab,Active,No,\n"
            + $"{code}-CAL-2,A-101,Torque wrench,Torque tool,Norbar,SN-2,5-50 Nm,0.1 Nm,±4%,Within ±4% at 20/60/100%,180,2026-03-01,{next},Acme Calibration,Active,Yes,Only 10-30 Nm\n"
            + $"{code}-CAL-3,A-102,Height gauge,Gauge,Starrett,SN-3,0-300 mm,0.01 mm,±0.03 mm,Within ±0.03 mm,365,2025-01-01,{past},Internal lab,Active,No,\n"
            + $"{code}-CAL-1,A-100,Digital caliper,Caliper,Mitutoyo,SN-1,0-150 mm,0.01 mm,±0.02 mm,Within ±0.02 mm,365,2026-01-15,{next},Internal lab,Active,No,\n";
        var id = await UploadAsync(author, site, "calibration master list.csv", Encoding.UTF8.GetBytes(csv), "master-equipment");
        var maps = (await author.GetElAsync($"/api/imports/{id}")).GetProperty("mappings").EnumerateArray().ToList();
        Assert.All(maps, m => Assert.False(m.GetProperty("targetField").ValueKind == JsonValueKind.Null, m.GetProperty("sourceName").GetString()));
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(2, dry.GetProperty("valid").GetInt32());
        Assert.Equal(1, dry.GetProperty("inError").GetInt32()); // the overdue gauge cannot be listed as active
        Assert.Equal(1, dry.GetProperty("duplicates").GetInt32());
        Assert.Contains("overdue", dry.GetProperty("preview")[2].GetProperty("problems")[0].GetString());
        await ApproveAsync(author, approver, id);

        var list = await author.GetElAsync($"/api/master-lists/calibration-equipment?q={code}");
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        var wrench = list.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("values").GetProperty("number").GetString() == $"{code}-CAL-2").GetProperty("values");
        Assert.Equal("±4%", wrench.GetProperty("accuracy").GetString());
        Assert.Equal("0.1 Nm", wrench.GetProperty("resolution").GetString());
        Assert.Equal("Acme Calibration", wrench.GetProperty("provider").GetString());
        Assert.Equal("Yes: Only 10-30 Nm", wrench.GetProperty("limited").GetString());
        Assert.Equal(next, wrench.GetProperty("next").GetString());

        // bulk update: the same list with a corrected accuracy updates the record; rollback puts the old value back
        var upd = await UploadAsync(author, site, "corrections.csv", Encoding.UTF8.GetBytes($"Equipment ID,Accuracy\n{code}-CAL-2,±3%\n"), "master-equipment");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{upd}/purpose", new { purpose = "master-list", targetModule = "master-equipment", targetTemplateId = (Guid?)null, templateRevision = (string?)null, existingRecords = "update", recordNumber = (string?)null })).StatusCode);
        dry = await (await author.PostOkAsync($"/api/imports/{upd}/validate")).JsonAsync();
        Assert.Equal(1, dry.GetProperty("toUpdate").GetInt32());
        Assert.Equal("±4%", dry.GetProperty("preview")[0].GetProperty("before").GetProperty("accuracy").GetString());
        await ApproveAsync(author, approver, upd);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.Equipment.AsNoTracking().SingleAsync(x => x.Number == $"{code}-CAL-2");
            Assert.Equal("±3%", e.Accuracy);
            Assert.Equal("Norbar", e.Manufacturer); // columns not in the file are left as they are
        }
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{upd}/rollback", new { reason = "Wrong certificate" })).StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("±4%", (await db.Equipment.AsNoTracking().SingleAsync(x => x.Number == $"{code}-CAL-2")).Accuracy);
        }

        // export keeps the list's columns; history shows the audit trail
        var xlsx = await (await author.GetAsync($"/api/master-lists/calibration-equipment/export?q={code}")).Content.ReadAsByteArrayAsync();
        using (var wb = new XLWorkbook(new MemoryStream(xlsx)))
        {
            var ws = wb.Worksheets.First();
            Assert.Equal("Equipment ID", ws.Cell(1, 1).GetString());
            Assert.Contains("Limited use", ws.Row(1).CellsUsed().Select(c => c.GetString()));
            Assert.Equal(3, ws.RowsUsed().Count());
        }
        var history = await author.GetElAsync("/api/master-lists/calibration-equipment/history");
        Assert.True(history.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Master_list_migration_keeps_governed_states_documents_arrive_as_drafts_and_suppliers_wait_for_approval()
    {
        var code = "MD" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var site = await app.CreateSiteAsync(code);
        var (_, author) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.SuppliersView, Permissions.SuppliersEdit, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.SuppliersView, Permissions.ImportsView, Permissions.ImportsApprove], site);

        // a master document list in Excel
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Master list");
        string[] head = ["Document number", "Title", "Category", "Revision", "Review date", "Expiration date", "Distribution", "Training requirement", "Related process"];
        for (var i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        object[][] rows =
        [
            [$"{code}-QP-001", "Control of documented information", "Procedure", "C", "2027-06-30", "2028-06-30", "All departments", "Yes", "Document control"],
            [$"{code}-QP-002", "Control of nonconforming output", "Procedure", "B", "2027-03-31", "", "Quality, Production", "No", "Nonconformance"],
        ];
        for (var r = 0; r < rows.Length; r++) for (var i = 0; i < head.Length; i++) ws.Cell(r + 2, i + 1).Value = rows[r][i]?.ToString();
        var ms = new MemoryStream(); wb.SaveAs(ms);
        var id = await UploadAsync(author, site, "master document list.xlsx", ms.ToArray(), "master-documents", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var dry = await (await author.PostOkAsync($"/api/imports/{id}/validate")).JsonAsync();
        Assert.Equal(2, dry.GetProperty("valid").GetInt32());
        await ApproveAsync(author, approver, id);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var d = await db.Documents.AsNoTracking().Include(x => x.Revisions).SingleAsync(x => x.Number == $"{code}-QP-001");
            Assert.Equal(DocumentStatus.Draft, d.Status);
            Assert.Equal("All departments", d.Distribution);
            Assert.Equal(new DateOnly(2028, 6, 30), d.ExpiresOn);
            Assert.Equal("Document control", d.RelatedProcess);
            var rev = d.Revisions.Single();
            Assert.Equal("C", rev.Revision);
            Assert.Equal(RevisionStatus.Draft, rev.Status);
            Assert.True(rev.TrainingRequired);
        }
        var list = await author.GetElAsync($"/api/master-lists/controlled-documents?q={code}&includeInactive=true");
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        Assert.Equal("Draft", list.GetProperty("rows")[0].GetProperty("values").GetProperty("status").GetString());

        // an approved supplier list: approved suppliers wait for supplier approval with that status proposed
        var csv = $"Supplier code,Legal name,Category,Supplied or approved scope,Criticality,Status,Approved on\n{code}-S1,Precision Castings Ltd,Castings,Aluminium die castings,High,Approved,2024-05-01\n{code}-S2,Fastener Supply Co,Fasteners,Standard fasteners,Low,Prospective,\n";
        var sid = await UploadAsync(author, site, "approved suppliers.csv", Encoding.UTF8.GetBytes(csv), "master-suppliers");
        await (await author.PostOkAsync($"/api/imports/{sid}/validate")).JsonAsync();
        await ApproveAsync(author, approver, sid);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var s1 = await db.Suppliers.AsNoTracking().SingleAsync(x => x.Code == $"{code}-S1");
            Assert.Equal(SupplierStatus.PendingApproval, s1.Status);
            Assert.Equal(SupplierStatus.Approved, s1.ProposedStatus);
            Assert.Contains("2024-05-01", s1.StatusReason);
            Assert.Equal(Criticality.High, s1.Criticality);
            Assert.Equal(SupplierStatus.Prospective, (await db.Suppliers.AsNoTracking().SingleAsync(x => x.Code == $"{code}-S2")).Status);
        }

        // saved views are per person and list
        var view = await (await author.PostOkAsync("/api/master-lists/suppliers/views", new { name = "High criticality", settings = "{\"q\":\"High\"}", isDefault = true })).Content.ReadFromJsonAsync<Guid>();
        var views = await author.GetElAsync("/api/master-lists/suppliers/views");
        Assert.Contains(views.EnumerateArray(), v => v.GetProperty("id").GetGuid() == view && v.GetProperty("isDefault").GetBoolean());
        Assert.Equal(0, (await approver.GetElAsync("/api/master-lists/suppliers/views")).GetArrayLength());
    }

    [Fact]
    public async Task Master_lists_follow_the_module_permissions_and_targets_match_the_purpose()
    {
        var site = await app.CreateSiteAsync();
        var (_, docsOnly) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.ImportsView, Permissions.ImportsEdit], site);
        var types = await docsOnly.GetElAsync("/api/master-lists");
        var keys = types.EnumerateArray().Select(t => t.GetProperty("key").GetString()).ToList();
        Assert.Contains("controlled-documents", keys);
        Assert.DoesNotContain("calibration-equipment", keys);
        Assert.Equal(HttpStatusCode.Forbidden, (await docsOnly.GetAsync("/api/master-lists/calibration-equipment")).StatusCode);

        // a master list target is chosen with the master list purpose, never as plain records
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Document number,Title\nX-1,Y\n")); file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "list.csv"); form.Add(new StringContent(site.ToString()), "siteId"); form.Add(new StringContent("records"), "mode"); form.Add(new StringContent("master-documents"), "targetModule");
        var r = await docsOnly.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/imports") { Content = form });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("master list", await r.Content.ReadAsStringAsync());

        // the import targets list says which are master lists
        var targets = await docsOnly.GetElAsync("/api/imports/targets");
        Assert.Contains(targets.EnumerateArray(), t => t.GetProperty("key").GetString() == "master-equipment" && t.GetProperty("masterList").GetBoolean());
        Assert.Contains(targets.EnumerateArray(), t => t.GetProperty("key").GetString() == "form-records" && !t.GetProperty("masterList").GetBoolean());
    }
}
