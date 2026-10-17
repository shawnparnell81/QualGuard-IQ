using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using static QualGuardIQ.Tests.GapClosurePfmeaImportTests;

namespace QualGuardIQ.Tests;

/// <summary>Gap 4: control plan bulk import — template, validation, duplicates, PFMEA / inspection plan / work instruction links, revision control, rollback, rejected export, permissions.</summary>
[Collection("api")]
public sealed class GapClosureControlPlanImportTests(TestApp app)
{
    const string Header = "Step,Process step,Operation,Machine / tool,Product characteristic,Process characteristic,Special characteristic,Specification,Tolerance,Measurement method,Inspection equipment,Sample size,Frequency,Control method,Reaction plan,Owner,Related PFMEA item,Related inspection plan,Related work instruction";

    static JsonElement Row(JsonElement res, int row) => res.GetProperty("rows").EnumerateArray().Single(x => x.Int("row") == row);
    static string Errors(JsonElement r) => string.Join(" | ", r.GetProperty("errors").EnumerateArray().Select(x => x.GetString()));

    sealed record Refs(Guid PfmeaId, Guid PfmeaLineId, Guid InspectionPlanId, string InspectionPlanNumber, Guid WiId, string WiNumber);

    async Task<Refs> RefsAsync(World w)
    {
        var pf = (await (await w.Eng.PostOkAsync("/api/apqp/pfmea", new { title = "Body PFMEA " + w.Tag, siteId = w.Site })).JsonAsync()).Id();
        const string pfHeader = "Step,Process function,Failure mode,Failure effect,S,Failure cause,Prevention controls,O,Detection controls,D,RPN,AP,Special characteristic,Recommended action,Responsible,Due date";
        await ImportOkAsync(w.Eng, "pfmea", pf, $"{pfHeader}\n20,Bore,Bore oversize,Leak,7,Tool wear,Tool life,3,Air gauge,3,,,,,,\n30,Wash,Chips left,Contamination,6,Nozzle blocked,,3,,4,,,,,,", dryRun: false);
        var pfLine = (await w.Eng.GetElAsync($"/api/apqp/pfmea/{pf}/lines")).EnumerateArray().Single(l => l.Str("failureMode") == "Bore oversize").Id();
        var (_, admin) = await app.CreateUserAsync([Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.DocumentsView, Permissions.DocumentsEdit, Permissions.InspectionView, Permissions.InspectionEdit], w.Site);
        var part = (await (await admin.PostOkAsync("/api/master/parts", new { partNumber = "CPI-" + w.Tag, name = "Valve body", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var ip = (await (await admin.PostOkAsync("/api/inspection/plans", new { title = "Final " + w.Tag, inspectionType = "Final", partId = part, siteId = w.Site,
            characteristics = new object[] { new { name = "Bore", kind = 1, specification = "25.00 ±0.03", requiresEquipment = false, isCritical = true } } })).JsonAsync()).Id();
        var wi = await admin.CreateDocumentAsync(w.Site, title: "Bore work instruction " + w.Tag);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ipNo = await db.InspectionPlans.AsNoTracking().Where(x => x.Id == ip).Select(x => x.Number).SingleAsync();
        var wiNo = await db.Documents.AsNoTracking().Where(x => x.Id == wi).Select(x => x.Number).SingleAsync();
        return new Refs(pf, pfLine, ip, ipNo, wi, wiNo);
    }

    static async Task<Guid> PlanAsync(World w, Guid? pfmeaId) =>
        (await (await w.Eng.PostOkAsync("/api/apqp/plans", new { title = "Body control plan " + w.Tag, siteId = w.Site, phase = 2, pfmeaId })).JsonAsync()).Id();

    [Fact]
    public async Task Template_validation_links_and_commit()
    {
        var w = await SetupAsync(app);
        var refs = await RefsAsync(w);
        var id = await PlanAsync(w, refs.PfmeaId);

        var tx = await w.Eng.Http.GetAsync("/api/apqp/plans/import/template");
        using (var wb = new XLWorkbook(new MemoryStream(await tx.Content.ReadAsByteArrayAsync())))
        {
            Assert.Equal("Step", wb.Worksheet("Lines").Cell(1, 1).GetString());
            Assert.Equal("Reaction plan", wb.Worksheet("Lines").Cell(1, 15).GetString());
        }
        var tc = await (await w.Eng.Http.GetAsync("/api/apqp/plans/import/template?format=csv")).Content.ReadAsStringAsync();
        Assert.StartsWith(Header, tc.TrimStart('\uFEFF'));
        var cols = await w.Eng.GetElAsync("/api/apqp/plans/import/columns");
        Assert.Contains(cols.EnumerateArray(), c => c.Str("key") == "inspectionEquipment");

        var csv = $"""
            {Header}
            20,Bore,Op 20A,Mill 3,Bore diameter,Spindle speed,CC,25.00,±0.03,Air gauge,AG-114,5,Hourly,X-bar R chart,Stop line and quarantine,{w.Email},20: Bore oversize,{refs.InspectionPlanNumber},{refs.WiNumber}
            30,Wash,,Washer 1,,Nozzle pressure,,3 bar min,,Pressure gauge,PG-2,1,Each shift,Checklist,Clean nozzle and rewash,,Chips left,,
            40,Deburr,,,,,,,,Visual,,5,Hourly,,Rework,,,,
            50,Assemble,,,Seal fitted,,ZZ,,,,,,Hourly,,Contain,nobody@nowhere.invalid,Missing mode,IP-NOPE,DOC-NOPE
            """;
        var dry = await ImportOkAsync(w.Eng, "plans", id, csv, dryRun: true);
        Assert.Equal(4, dry.Int("totalRows"));
        Assert.Equal(2, dry.Int("accepted")); Assert.Equal(2, dry.Int("rejected"));
        Assert.Empty((await w.Eng.GetElAsync($"/api/apqp/plans/{id}/lines")).EnumerateArray());
        Assert.Contains("Enter a product characteristic or a process characteristic", Errors(Row(dry, 4)));
        var e5 = Errors(Row(dry, 5));
        Assert.Contains("Method is required", e5);
        Assert.Contains("Sample size is required", e5);
        Assert.Contains("Special characteristic \"ZZ\" is not a configured classification", e5);
        Assert.Contains("\"nobody@nowhere.invalid\" is not an active user", e5);
        Assert.Contains("PFMEA item \"Missing mode\" is not a failure mode of the linked PFMEA", e5);
        Assert.Contains("Inspection plan \"IP-NOPE\" does not exist", e5);
        Assert.Contains("Work instruction \"DOC-NOPE\" is not a controlled document", e5);

        var refused = await ImportOkAsync(w.Eng, "plans", id, csv, dryRun: false);
        Assert.Equal("Refused", refused.Str("status"));
        Assert.Empty((await w.Eng.GetElAsync($"/api/apqp/plans/{id}/lines")).EnumerateArray());

        var ok = await ImportOkAsync(w.Eng, "plans", id, csv, dryRun: false, new() { ["allowPartial"] = "true" });
        Assert.Equal("Committed", ok.Str("status"));
        var lines = (await w.Eng.GetElAsync($"/api/apqp/plans/{id}/lines")).EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        var bore = lines.Single(l => l.Int("stepSequence") == 20);
        Assert.Equal("Bore diameter", bore.Str("characteristic")); Assert.Equal("Product", bore.Str("characteristicType"));
        Assert.Equal("Spindle speed", bore.Str("processCharacteristic")); Assert.Equal("Op 20A", bore.Str("operation"));
        Assert.Equal("±0.03", bore.Str("tolerance")); Assert.Equal("AG-114", bore.Str("inspectionEquipment"));
        Assert.Equal(refs.PfmeaLineId, bore.Id("pfmeaLineId")); Assert.Equal(refs.InspectionPlanId, bore.Id("inspectionPlanId"));
        Assert.Equal(refs.WiId, bore.Id("workInstructionId")); Assert.Equal(w.UserId, bore.Id("ownerId"));
        var wash = lines.Single(l => l.Int("stepSequence") == 30);
        Assert.Equal("Process", wash.Str("characteristicType")); Assert.Equal("Nozzle pressure", wash.Str("characteristic"));

        // Rejected-row export and audit.
        var rejected = await (await w.Eng.Http.GetAsync($"/api/apqp/imports/{ok.Id("importId")}/rejected.csv")).Content.ReadAsStringAsync();
        Assert.Equal(3, rejected.TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Contains("is not a controlled document", rejected);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Contains("2 line(s) imported", (await db.AuditEntries.AsNoTracking().SingleAsync(a => a.EntityId == id && a.Action == "LinesImported")).Description);

        // Duplicates against existing lines and within the file.
        var dup = await ImportOkAsync(w.Eng, "plans", id, $"{Header}\n20,Bore,,,bore  DIAMETER,Spindle speed,,,,Air gauge,,5,Hourly,,Stop,,,,\n60,Pack,,,Label,,,,,Visual,,1,Each box,,Relabel,,,,\n60,Pack,,,Label,,,,,Visual,,1,Each box,,Relabel,,,,", dryRun: true);
        Assert.Equal("Duplicate", Row(dup, 2).Str("status")); Assert.Equal("Accepted", Row(dup, 3).Str("status")); Assert.Equal("Duplicate", Row(dup, 4).Str("status"));
        Assert.Equal(2, dup.Int("duplicates"));
    }

    [Fact]
    public async Task Ambiguous_or_unlinked_pfmea_items_xlsx_mapping_and_missing_columns()
    {
        var w = await SetupAsync(app);
        var unlinked = await PlanAsync(w, null);
        var r = await ImportOkAsync(w.Eng, "plans", unlinked, $"{Header}\n10,Cut,,,Length,,,,,Tape,,1,Each,,Recut,,Short,,", dryRun: true);
        Assert.Contains("Link the control plan to a PFMEA before referencing PFMEA items", Errors(Row(r, 2)));

        var pf = (await (await w.Eng.PostOkAsync("/api/apqp/pfmea", new { title = "Dup PFMEA " + w.Tag, siteId = w.Site })).JsonAsync()).Id();
        const string pfHeader = "Step,Process function,Failure mode,Failure effect,S,Failure cause,Prevention controls,O,Detection controls,D,RPN,AP,Special characteristic,Recommended action,Responsible,Due date";
        await ImportOkAsync(w.Eng, "pfmea", pf, $"{pfHeader}\n10,Cut,Short,Scrap,5,Stop moved,,3,,3,,,,,,\n20,Trim,Short,Scrap,5,Fixture,,3,,3,,,,,,", dryRun: false);
        var id = await PlanAsync(w, pf);
        var amb = await ImportOkAsync(w.Eng, "plans", id, $"{Header}\n10,Cut,,,Length,,,,,Tape,,1,Each,,Recut,,Short,,\n10,Cut,,,Width,,,,,Tape,,1,Each,,Recut,,10: Short,,", dryRun: true);
        Assert.Contains("matches 2 lines", Errors(Row(amb, 2)));
        Assert.Equal("Accepted", Row(amb, 3).Str("status"));

        // Customer workbook with its own headers and an explicit mapping.
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("CP");
        string[] head = ["Part/Process Number", "Process Name / Operation Description", "Machine, Device, Jig, Tools", "Product", "Evaluation/Measurement Technique", "Size", "Freq", "Reaction Plan", "Gauge No"];
        string[] val = ["10", "Cut", "Saw 2", "Length", "Tape", "1", "Each bar", "Recut", "T-9"];
        for (var i = 0; i < head.Length; i++) { ws.Cell(1, i + 1).Value = head[i]; ws.Cell(2, i + 1).Value = val[i]; }
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        var map = JsonSerializer.Serialize(new Dictionary<string, string> { ["inspectionEquipment"] = "Gauge No" });
        var x = await UploadAsync(w.Eng, $"/api/apqp/plans/{id}/import", ms.ToArray(), "customer-cp.xlsx", new() { ["dryRun"] = "false", ["mapping"] = map });
        Assert.True(x.IsSuccessStatusCode, await x.Content.ReadAsStringAsync());
        var xr = await x.JsonAsync();
        Assert.Equal("Committed", xr.Str("status"));
        var line = (await w.Eng.GetElAsync($"/api/apqp/plans/{id}/lines")).EnumerateArray().Single();
        Assert.Equal("T-9", line.Str("inspectionEquipment")); Assert.Equal("Saw 2", line.Str("machineOrTool"));

        var miss = await ImportOkAsync(w.Eng, "plans", id, "Step,Process step\n10,Cut", dryRun: true);
        Assert.Equal("Refused", miss.Str("status"));
        Assert.Contains("Reaction plan", miss.GetProperty("missingRequired").ToString());
    }

    [Fact]
    public async Task Released_plan_is_immutable_revision_is_atomic_and_unauthorized_users_are_refused()
    {
        var w = await SetupAsync(app);
        var id = await PlanAsync(w, null);
        await ImportOkAsync(w.Eng, "plans", id, $"{Header}\n10,Cut,,,Length,,,,,Tape,,1,Each,,Recut,,,,", dryRun: false);
        await ReleaseAsync(w, "plans", id);
        var good = $"{Header}\n20,Drill,,,Hole size,,,,,Pin gauge,,5,Hourly,,Stop and contain,,,,";

        var r = await UploadAsync(w.Eng, $"/api/apqp/plans/{id}/import", Encoding.UTF8.GetBytes(good), "a.csv", new() { ["dryRun"] = "false" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("Released revisions never change", (await ImportOkAsync(w.Eng, "plans", id, good, dryRun: true)).Str("message"));

        // Rollback: a refused file with createRevision leaves no new revision.
        var refused = await ImportOkAsync(w.Eng, "plans", id, $"{good}\n30,Tap,,,,,,,,Thread gauge,,5,Hourly,,Stop,,,,", dryRun: false, new() { ["createRevision"] = "true", ["revisionReason"] = "Add drilling" });
        Assert.Equal("Refused", refused.Str("status"));
        Assert.Equal(1, (await w.Eng.GetElAsync($"/api/apqp/plans/{id}/history")).GetArrayLength());

        var ok = await ImportOkAsync(w.Eng, "plans", id, good, dryRun: false, new() { ["createRevision"] = "true", ["revisionReason"] = "Add drilling" });
        Assert.Equal("Committed", ok.Str("status")); Assert.Equal(2, ok.Int("documentRevision"));
        var rev2 = ok.Id("documentId");
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/apqp/plans/{rev2}/lines")).GetArrayLength());
        Assert.Equal(1, (await w.Eng.GetElAsync($"/api/apqp/plans/{id}/lines")).GetArrayLength());

        var (_, viewer) = await app.CreateUserAsync([Permissions.ApqpView], w.Site);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(viewer, $"/api/apqp/plans/{rev2}/import", Encoding.UTF8.GetBytes(good), "a.csv", new() { ["dryRun"] = "true" })).StatusCode);
        var other = await app.CreateSiteAsync();
        var (_, elsewhere) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpEdit], other);
        var e = await UploadAsync(elsewhere, $"/api/apqp/plans/{rev2}/import", Encoding.UTF8.GetBytes(good), "a.csv", new() { ["dryRun"] = "false" });
        Assert.True(e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, e.StatusCode.ToString());
    }
}
