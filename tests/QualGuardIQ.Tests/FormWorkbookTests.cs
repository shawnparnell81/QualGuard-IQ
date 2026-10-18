using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Excel round trip: generated offline workbook, controlled completion, reimport with integrity, structure and duplicate checks.</summary>
[Collection("api")]
public sealed class FormWorkbookTests(TestApp app)
{
    static string Schema() => JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        sections = new object[] { new { key = "head", title = "Lot", columns = 2 } },
        fields = new object[]
        {
            new { key = "lot", label = "Lot number", type = "text", section = "head", required = true },
            new { key = "qty", label = "Quantity", type = "integer", section = "head", min = 1, max = 10000 },
            new { key = "received", label = "Received on", type = "date", section = "head" },
            new { key = "result", label = "Result", type = "choice", section = "head", options = new[] { "Accept", "Reject" } },
            new { key = "double_qty", label = "Twice the quantity", type = "calc", section = "head", formula = "qty*2" },
        },
        tables = new object[]
        {
            new { key = "dims", title = "Dimensions", section = "head", columns = new object[]
            {
                new { key = "name", label = "Characteristic", type = "text" },
                new { key = "nominal", label = "Nominal", type = "number" },
                new { key = "actual", label = "Actual", type = "number" },
                new { key = "dev", label = "Deviation", type = "calc", formula = "actual-nominal" },
            } },
        },
    });

    async Task<(Guid Site, ApiClient Author, Guid Template)> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView], site);
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Receiving lot " + Guid.NewGuid().ToString("N")[..6]);
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = Schema() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);
        return (site, author, tplId);
    }

    static async Task<HttpResponseMessage> SubmitAsync(ApiClient c, Guid tpl, Guid site, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var f = new ByteArrayContent(bytes); f.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(f, "file", "completed.xlsx");
        form.Add(new StringContent(site.ToString()), "siteId");
        return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/forms/templates/{tpl}/workbook") { Content = form });
    }

    static byte[] Edit(byte[] src, Action<XLWorkbook> change)
    {
        using var wb = new XLWorkbook(new MemoryStream(src));
        change(wb);
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }

    [Fact]
    public async Task Offline_workbook_round_trips_into_a_recalculated_record_once()
    {
        var (site, author, tpl) = await SetupAsync();
        var exported = await (await author.GetAsync($"/api/forms/templates/{tpl}/workbook")).Content.ReadAsByteArrayAsync();
        using (var wb = new XLWorkbook(new MemoryStream(exported)))
        {
            Assert.Equal(["Form", "Dimensions", "_qg"], wb.Worksheets.Select(w => w.Name));
            Assert.Equal(XLWorksheetVisibility.VeryHidden, wb.Worksheet("_qg").Visibility);
            var form = wb.Worksheet("Form");
            Assert.True(form.IsProtected);
            Assert.Equal("Lot number *", form.Cell(6, 1).GetString());
            Assert.False(form.Cell(6, 2).Style.Protection.Locked);
            Assert.True(form.Cell(10, 2).Style.Protection.Locked); // calculated
            Assert.Contains("Accept", form.Cell(9, 2).GetDataValidation().MinValue);
        }
        var filled = Edit(exported, wb =>
        {
            var f = wb.Worksheet("Form");
            f.Cell(6, 2).Value = "L-2026-77"; f.Cell(7, 2).Value = 25; f.Cell(8, 2).Value = new DateTime(2026, 10, 15); f.Cell(9, 2).Value = "Accept";
            var d = wb.Worksheet("Dimensions");
            d.Cell(2, 1).Value = "Bore"; d.Cell(2, 2).Value = 10; d.Cell(2, 3).Value = 10.04;
            d.Cell(3, 1).Value = "Length"; d.Cell(3, 2).Value = 50; d.Cell(3, 3).Value = 49.9;
        });
        var r = await SubmitAsync(author, tpl, site, filled);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var sub = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, sub.GetProperty("tableRows").GetInt32());
        var recId = sub.GetProperty("recordId").GetGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rec = await db.FormRecords.SingleAsync(x => x.Id == recId);
            var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(rec.ValuesJson)!;
            Assert.Equal("L-2026-77", values["lot"]);
            Assert.Equal("2026-10-15", values["received"]);
            Assert.Equal("50", values["double_qty"]);
            Assert.Contains("\"dev\":\"0.04\"", values["dims"]);
            var evidence = await db.Attachments.SingleAsync(a => a.OwnerType == "FormRecord" && a.OwnerId == recId);
            Assert.True(evidence.IsLocked);
            Assert.Equal("Offline workbook", evidence.Category);
        }
        Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(author, tpl, site, filled)).StatusCode);
        // the same copy saved again with other values is still the same copy
        Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(author, tpl, site, Edit(filled, wb => wb.Worksheet("Form").Cell(7, 2).Value = 30))).StatusCode);
    }

    [Fact]
    public async Task Altered_structure_forged_integrity_data_and_foreign_workbooks_are_rejected()
    {
        var (site, author, tpl) = await SetupAsync();
        var exported = await (await author.GetAsync($"/api/forms/templates/{tpl}/workbook")).Content.ReadAsByteArrayAsync();
        var relabeled = Edit(exported, wb => { var f = wb.Worksheet("Form"); f.Cell(6, 1).Value = "Batch"; });
        var r = await SubmitAsync(author, tpl, site, relabeled);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("protected parts", await r.Content.ReadAsStringAsync());
        var unlocked = Edit(exported, wb => { var f = wb.Worksheet("Form"); f.Cell(10, 2).Style.Protection.Locked = false; f.Cell(10, 2).Value = 999; });
        Assert.Equal(HttpStatusCode.BadRequest, (await SubmitAsync(author, tpl, site, unlocked)).StatusCode);
        var forged = Edit(exported, wb => wb.Worksheet("_qg").Cell(1, 1).Value = "eyJmb3JnZWQiOnRydWV9");
        r = await SubmitAsync(author, tpl, site, forged);
        Assert.Contains("integrity data", await r.Content.ReadAsStringAsync());
        using var plain = new XLWorkbook(); plain.AddWorksheet("Form").Cell(1, 1).Value = "x";
        using var ms = new MemoryStream(); plain.SaveAs(ms);
        r = await SubmitAsync(author, tpl, site, ms.ToArray());
        Assert.Contains("not generated by QualGuard IQ", await r.Content.ReadAsStringAsync());
    }
}
