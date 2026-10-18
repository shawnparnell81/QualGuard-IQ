using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Infrastructure.Modules.Imports.Conversion;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Form conversion engine: field detection, confidence at every level, review with corrections, form families and the conversion pipeline.</summary>
[Collection("api")]
public sealed class ConversionEngineTests(TestApp app)
{
    [Theory]
    [InlineData("Supplier name", "text", false, "supplier")]
    [InlineData("Inspected by", "text", false, "user")]
    [InlineData("Qty rejected", "text", false, "integer")]
    [InlineData("Operator signature", "text", false, "signature")]
    [InlineData("Part number", "text", false, "part")]
    [InlineData("Gage ID", "text", false, "equipment")]
    [InlineData("NCR number", "text", false, "record")]
    [InlineData("Date and time of event", "text", false, "datetime")]
    [InlineData("Description of defect", "text", false, "longtext")]
    [InlineData("Yield %", "text", false, "percent")]
    [InlineData("Scrap cost", "text", false, "currency")]
    [InlineData("Photo of label", "text", false, "photo")]
    [InlineData("Inspection date", "date", true, "date")]
    [InlineData("Measured diameter", "number", true, "number")]
    public void Field_detection_suggests_the_candidate_type_from_the_label(string label, string type, bool evidence, string expected)
    {
        Assert.Equal(expected, FieldDetection.Suggest(label, type, evidence).Type);
    }

    [Fact]
    public void Source_evidence_outranks_label_guesses_and_unlabelled_fields_are_low_confidence()
    {
        var s = FieldDetection.Suggest("Lot qty", "number", true); // number format in the cell: keep it
        Assert.Equal("number", s.Type); Assert.True(s.Confidence >= ConversionEngine.High);
        var c = FieldDetection.Suggest("Result", "choice", true, ["Pass", "Fail"]);
        Assert.Equal("choice", c.Type); Assert.Equal("high", ConversionEngine.Level(c.Confidence));
        var blank = FieldDetection.Suggest("Field 3", "text", false);
        Assert.Equal("low", ConversionEngine.Level(blank.Confidence));
        Assert.Equal(27, FieldDetection.Candidates.Count);
    }

    [Fact]
    public async Task Excel_conversion_is_reviewed_corrected_and_checked_against_a_form_family_before_approval()
    {
        var (site, author, approver) = await ExcelImportTests.UsersAsync(app);
        var id = await ExcelImportTests.UploadAsync(author, site, "Receiving inspection.xlsx", ExcelConversionTests.InspectionWorkbook().ToArray(), "template");
        var job = await author.GetElAsync($"/api/imports/{id}");
        var a = job.GetProperty("analysis");
        var items = a.GetProperty("detections").EnumerateArray().ToList();
        string[] kinds = ["section", "field", "checkbox", "formula", "table", "column"];
        foreach (var k in kinds) Assert.Contains(items, i => i.GetProperty("kind").GetString() == k);
        var conf = a.GetProperty("confidence");
        foreach (var level in new[] { "document", "fields", "formulas", "tables" }) Assert.False(string.IsNullOrEmpty(conf.GetProperty(level).GetProperty("level").GetString()));
        Assert.Equal("Inspection", conf.GetProperty("pages")[0].GetProperty("name").GetString());
        Assert.True(conf.GetProperty("sections").GetArrayLength() >= 1);

        // the label suggests a part reference; it is offered, not applied
        var part = items.Single(i => i.GetProperty("id").GetString() == "field:part_number");
        Assert.Equal("text", part.GetProperty("type").GetString());
        Assert.Equal("part", part.GetProperty("suggested").GetString());

        // a correction that would break a formula is refused with the reason
        var bad = await author.PutAsync($"/api/imports/{id}/detections", new { items = new[] { new { id = "field:lot_qty", include = false } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("Sample size", await bad.Content.ReadAsStringAsync());

        // accept the suggestion, rename a field, confirm everything
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/detections", new { items = new object[] { new { id = "field:part_number", type = "part" }, new { id = "field:disposition", label = "Lot disposition" } }, confirmAll = true })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        a = job.GetProperty("analysis");
        Assert.Equal(0, a.GetProperty("review").GetProperty("required").GetInt32());
        var def = FormDefinitions.Parse(a.GetProperty("conversion").GetProperty("definition").GetRawText());
        Assert.Equal("part", def.Fields.Single(f => f.Key == "part_number").Type);
        Assert.Equal("Lot disposition", def.Fields.Single(f => f.Key == "disposition").Label);
        Assert.Equal(["Pass", "Fail", "Hold"], def.Fields.Single(f => f.Key == "disposition").Options);
        Assert.Contains(job.GetProperty("history").EnumerateArray(), h => h.GetProperty("event").GetString() == "ConversionReviewed");

        // form family: an NCR form; the engine reports which NCR elements were found
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/imports/{id}/conversion-target", new { target = "brochure" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/conversion-target", new { target = "ncr" })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        var profile = job.GetProperty("analysis").GetProperty("profile");
        Assert.Equal("ncr", profile.GetProperty("key").GetString());
        var elements = profile.GetProperty("elements").EnumerateArray().ToList();
        Assert.Equal("part_number", elements.Single(e => e.GetProperty("element").GetString() == "Part").GetProperty("fieldKey").GetString());
        Assert.Equal("disposition", elements.Single(e => e.GetProperty("element").GetString() == "Disposition").GetProperty("fieldKey").GetString());
        Assert.Contains(job.GetProperty("issues").EnumerateArray(), i => i.GetProperty("issueCode").GetString() == "PROFILE");
        // the review is kept when the file is converted again
        Assert.Equal("part", FormDefinitions.Parse(job.GetProperty("analysis").GetProperty("conversion").GetProperty("definition").GetRawText()).Fields.Single(f => f.Key == "part_number").Type);

        var steps = await author.GetElAsync($"/api/imports/{id}/pipeline");
        Assert.Equal(23, steps.GetArrayLength());
        Assert.Equal("Publish controlled template", steps[22].GetProperty("name").GetString());
        Assert.Equal("pending", steps[21].GetProperty("status").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/imports/{id}/approve", new { acceptFidelityReport = true })).StatusCode);
        job = await author.GetElAsync($"/api/imports/{id}");
        var docId = Guid.Parse(job.GetProperty("summary").GetProperty("documentId").GetString()!);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal("ncr", (await db.Documents.SingleAsync(d => d.Id == docId)).ModuleKey);
        }
        steps = await author.GetElAsync($"/api/imports/{id}/pipeline");
        Assert.Equal("done", steps[21].GetProperty("status").GetString());
        Assert.Equal("attention", steps[22].GetProperty("status").GetString()); // a draft template: published through template approval
    }

    [Fact]
    public async Task A_formula_that_stays_only_in_the_original_must_be_confirmed_before_the_conversion_is_submitted()
    {
        var (site, author, _) = await ExcelImportTests.UsersAsync(app);
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Check");
        ws.Cell("A1").Value = "Supplier check"; ws.Cell("A3").Value = "Supplier name:"; ws.Cell("B3").Style.Border.BottomBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
        ws.Cell("A4").Value = "Lookup:"; ws.Cell("B4").FormulaA1 = "INDIRECT(\"B3\")";
        var ms = new MemoryStream(); wb.SaveAs(ms);
        var id = await ExcelImportTests.UploadAsync(author, site, "Supplier check.xlsx", ms.ToArray(), "template");
        var a = (await author.GetElAsync($"/api/imports/{id}")).GetProperty("analysis");
        var items = a.GetProperty("detections").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("kind").GetString() == "formula" && i.GetProperty("mustConfirm").GetBoolean());
        Assert.Equal("supplier", items.Single(i => i.GetProperty("label").GetString() == "Supplier name").GetProperty("suggested").GetString());
        Assert.True(a.GetProperty("review").GetProperty("required").GetInt32() > 0);
        var r = await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("low confidence", await r.Content.ReadAsStringAsync());
        var steps = await author.GetElAsync($"/api/imports/{id}/pipeline");
        Assert.Equal("attention", steps[16].GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/imports/{id}/detections", new { items = Array.Empty<object>(), confirmAll = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/imports/{id}/submit", new { skipInvalidRows = false })).StatusCode);
    }

    [Fact]
    public async Task Word_conversion_gets_the_same_review_and_a_records_import_has_no_conversion_steps()
    {
        var (site, author, _) = await ExcelImportTests.UsersAsync(app);
        var docx = new DocxBuilder().Add(DocxBuilder.Text("Supplier visit report", "Heading1"))
            .Add(DocxBuilder.P(DocxBuilder.Run("Supplier: ") + DocxBuilder.ControlText("supplier", "Supplier")))
            .Add(DocxBuilder.P(DocxBuilder.Run("Visit date: ") + DocxBuilder.ControlDate("visit_date", "Visit date"))).Build();
        var id = await ExcelImportTests.UploadAsync(author, site, "Supplier visit.docx", docx, "template", type: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        var job = await author.GetElAsync($"/api/imports/{id}");
        var items = job.GetProperty("analysis").GetProperty("detections").EnumerateArray().ToList();
        var sup = items.First(i => i.GetProperty("label").GetString()!.StartsWith("Supplier") && i.GetProperty("kind").GetString() == "field");
        Assert.Equal("supplier", sup.GetProperty("suggested").GetString());
        Assert.Equal("content-control", sup.GetProperty("source").GetString());
        Assert.Contains(items, i => i.GetProperty("type").GetString() == "date");

        var steps = await author.GetElAsync($"/api/imports/{id}/pipeline");
        Assert.Equal("done", steps[14].GetProperty("status").GetString());

        var csv = await ExcelImportTests.UploadAsync(author, site, "rows.csv", "a,b\n1,2\n"u8.ToArray(), "records", "form-records", type: "text/csv");
        steps = await author.GetElAsync($"/api/imports/{csv}/pipeline");
        Assert.Equal("not-applicable", steps[10].GetProperty("status").GetString());
    }
}
