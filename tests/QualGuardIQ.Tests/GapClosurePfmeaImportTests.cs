using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 3: PFMEA bulk import — template, mapping, dry run, row validation, duplicates, RPN/AP recalculation, revision control, rollback, history, audit.</summary>
[Collection("api")]
public sealed class GapClosurePfmeaImportTests(TestApp app)
{
    internal sealed record World(Guid Site, Guid UserId, string Email, ApiClient Eng, ApiClient Approver, string Tag);

    internal static async Task<World> SetupAsync(TestApp app)
    {
        var site = await app.CreateSiteAsync();
        var (uid, eng) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpEdit, Permissions.InspectionView, Permissions.InspectionEdit, Permissions.SpcView, Permissions.MasterDataView, Permissions.DocumentsView], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpApprove], site);
        var email = await EmailOfAsync(app, uid);
        return new World(site, uid, email, eng, approver, Guid.NewGuid().ToString("N")[..6].ToUpper());
    }

    static async Task<string> EmailOfAsync(TestApp app, Guid uid)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().FirstAsync(u => u.Id == uid)).Email!;
    }

    internal static async Task<HttpResponseMessage> UploadAsync(ApiClient c, string url, byte[] content, string fileName, Dictionary<string, string> form)
    {
        var mp = new MultipartFormDataContent();
        var file = new ByteArrayContent(content); file.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".csv") ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        mp.Add(file, "file", fileName);
        foreach (var (k, v) in form) mp.Add(new StringContent(v), k);
        return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = mp });
    }

    internal static async Task<JsonElement> ImportOkAsync(ApiClient c, string kind, Guid id, string csv, bool dryRun, Dictionary<string, string>? extra = null, string name = "lines.csv")
    {
        var form = new Dictionary<string, string> { ["dryRun"] = dryRun ? "true" : "false" };
        foreach (var (k, v) in extra ?? []) form[k] = v;
        var r = await UploadAsync(c, $"/api/apqp/{kind}/{id}/import", Encoding.UTF8.GetBytes(csv), name, form);
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return await r.JsonAsync();
    }

    static async Task<Guid> PfmeaAsync(World w, string title, Guid? flowId = null) =>
        (await (await w.Eng.PostOkAsync("/api/apqp/pfmea", new { title = title + " " + w.Tag, siteId = w.Site, processFlowId = flowId })).JsonAsync()).Id();

    internal static async Task ReleaseAsync(World w, string kind, Guid id)
    {
        var doc = (await w.Eng.GetElAsync($"/api/apqp/{kind}/{id}")).GetProperty("document");
        var s = await w.Eng.PostAsync($"/api/apqp/{kind}/{id}/submit");
        Assert.True(s.StatusCode == HttpStatusCode.NoContent, await s.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await w.Approver.SignAsync(await w.Approver.PendingApprovalIdAsync(doc.Str("number")))).StatusCode);
    }

    const string Header = "Step,Process function,Failure mode,Failure effect,S,Failure cause,Prevention controls,O,Detection controls,D,RPN,AP,Special characteristic,Recommended action,Responsible,Due date";

    static JsonElement Row(JsonElement res, int row) => res.GetProperty("rows").EnumerateArray().Single(x => x.Int("row") == row);
    static string Errors(JsonElement r) => string.Join(" | ", r.GetProperty("errors").EnumerateArray().Select(x => x.GetString()));

    [Fact]
    public async Task Template_dry_run_and_commit_recalculate_rpn_and_ap_and_reject_invalid_rows()
    {
        var w = await SetupAsync(app);
        var id = await PfmeaAsync(w, "Pump housing PFMEA");
        // Templates: xlsx with a Help sheet, csv with the field labels.
        var tx = await w.Eng.Http.GetAsync("/api/apqp/pfmea/import/template");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", tx.Content.Headers.ContentType!.MediaType);
        using (var wb = new XLWorkbook(new MemoryStream(await tx.Content.ReadAsByteArrayAsync())))
        {
            Assert.Equal("Step", wb.Worksheet("Lines").Cell(1, 1).GetString());
            Assert.True(wb.Worksheet("Help").RowsUsed().Count() > 15);
        }
        var tc = await (await w.Eng.Http.GetAsync("/api/apqp/pfmea/import/template?format=csv")).Content.ReadAsStringAsync();
        Assert.StartsWith("Step,Process function,Failure mode", tc.TrimStart('\uFEFF'));

        var due = TestDates.Today.AddDays(30).ToString("yyyy-MM-dd");
        var csv = $"""
            {Header}
            10,Press bearing,Bearing too shallow,Noise at EOL,7,Press stop worn,Hard stop PM,4,Depth gauge 100%,3,999,L,SC,,,
            20,Torque cover bolts,Bolt under-torqued,Cover leak,9,Tool not calibrated,Calibration schedule,5,Audit torque,6,,,CC,Add DC tool with feedback,{w.Email},{due}
            30,Leak test,Leak not detected,Field leak,11,Test pressure low,,3,,4,,,,,,
            40,Pack,,Damaged part,4,Wrong dunnage,,2,,2,,,,,,
            50,Label,Wrong label,Mis-shipment,6,Label template wrong,,x,,3,,,ZZ,,,
            60,Final inspect,Missed burr,Customer cut,8,Inspector fatigue,,4,Visual,7,,,,Add vision system,nobody@nowhere.invalid,{due}
            """;
        var dry = await ImportOkAsync(w.Eng, "pfmea", id, csv, dryRun: true);
        Assert.Equal("DryRun", dry.Str("status"));
        Assert.Equal(6, dry.Int("totalRows"));
        Assert.Equal(2, dry.Int("accepted")); Assert.Equal(4, dry.Int("rejected"));
        Assert.Empty((await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}/lines")).EnumerateArray()); // dry run writes nothing
        // RPN and AP recalculated; the file's own RPN/AP is ignored with a warning.
        var r2 = Row(dry, 2);
        Assert.Equal(84, r2.GetProperty("calculated").Int("rpn"));
        Assert.Contains("replaced by the calculated 84", r2.GetProperty("warnings").ToString());
        Assert.Contains("replaced by the calculated Medium", r2.GetProperty("warnings").ToString());
        Assert.Equal("High", Row(dry, 3).GetProperty("calculated").Str("actionPriority"));
        Assert.Equal(270, Row(dry, 3).GetProperty("calculated").Int("rpn"));
        // Invalid score, missing data, non-numeric score, unknown classification, unknown responsible person.
        Assert.Contains("Severity must be between 1 and 10", Errors(Row(dry, 4)));
        Assert.Contains("Failure mode is required", Errors(Row(dry, 5)));
        Assert.Contains("Occurrence (O) \"x\" is not a whole number", Errors(Row(dry, 6)));
        Assert.Contains("Special characteristic \"ZZ\" is not a configured classification", Errors(Row(dry, 6)));
        Assert.Contains("\"nobody@nowhere.invalid\" is not an active user", Errors(Row(dry, 7)));

        // Commit refuses the whole file while rows are rejected (nothing written), unless only valid rows are requested.
        var refused = await ImportOkAsync(w.Eng, "pfmea", id, csv, dryRun: false);
        Assert.Equal("Refused", refused.Str("status"));
        Assert.Empty((await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}/lines")).EnumerateArray());
        var partial = await ImportOkAsync(w.Eng, "pfmea", id, csv, dryRun: false, new() { ["allowPartial"] = "true" });
        Assert.Equal("Committed", partial.Str("status"));
        var lines = (await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}/lines")).EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        var torque = lines.Single(l => l.Str("failureMode") == "Bolt under-torqued");
        Assert.Equal(270, torque.Int("rpn")); Assert.Equal(2, torque.Int("priority")); Assert.Equal(w.UserId, torque.Id("responsibleId"));
        Assert.Equal(84, lines.Single(l => l.Str("failureMode") == "Bearing too shallow").Int("rpn"));

        // Rejected-row export: original columns plus row number and errors.
        var rejected = await (await w.Eng.Http.GetAsync($"/api/apqp/imports/{partial.Id("importId")}/rejected.csv")).Content.ReadAsStringAsync();
        var rl = rejected.TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5, rl.Length);
        Assert.StartsWith("Row,Outcome,Errors,Step,", rl[0]);
        Assert.Contains("Severity must be between 1 and 10", rejected);

        // History and audit.
        var hist = await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}/imports");
        Assert.Equal(["Committed", "Refused", "DryRun"], hist.EnumerateArray().Select(x => x.Str("status")).ToArray());
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditEntries.AsNoTracking().SingleAsync(a => a.EntityId == id && a.Action == "LinesImported");
        Assert.Contains("2 line(s) imported", audit.Description);
        Assert.Contains("sha256", audit.Changes);
    }

    [Fact]
    public async Task Duplicates_mapping_xlsx_formulas_and_process_flow_steps_are_checked()
    {
        var w = await SetupAsync(app);
        var flow = (await (await w.Eng.PostOkAsync("/api/apqp/flows", new { title = "Flow " + w.Tag, siteId = w.Site })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await w.Eng.PutAsync($"/api/apqp/flows/{flow}/lines", new[] {
            new { sequence = 10, name = "Machine", stepType = "Operation", inputs = (string?)null, outputs = (string?)null, ownerId = (Guid?)null, workInstructionId = (Guid?)null, inspectionPlanId = (Guid?)null } })).StatusCode);
        var id = await PfmeaAsync(w, "Shaft PFMEA", flow);
        await ImportOkAsync(w.Eng, "pfmea", id, $"{Header}\n10,Turn OD,OD oversize,Assembly interference,6,Tool wear,Tool life counter,3,Air gauge,4,,,,,,", dryRun: false);

        // Same line again (existing), twice in the file, and a step the process flow does not have.
        var again = await ImportOkAsync(w.Eng, "pfmea", id, $"""
            {Header}
            10,Turn OD,OD Oversize,Assembly interference,6,Tool  wear,Tool life counter,3,Air gauge,4,,,,,,
            10,Turn OD,Surface finish rough,Noise,4,Feed too high,,3,,4,,,,,,
            10,Turn OD,Surface finish rough,Noise,4,Feed too high,,3,,4,,,,,,
            99,Grind,Taper,Bearing wear,6,Wheel dressing,,3,,4,,,,,,
            """, dryRun: true);
        Assert.Equal("Duplicate", Row(again, 2).Str("status"));
        Assert.Contains("already has this line", Row(again, 2).GetProperty("warnings").ToString());
        Assert.Equal("Accepted", Row(again, 3).Str("status"));
        Assert.Equal("Duplicate", Row(again, 4).Str("status"));
        Assert.Contains("Step 99 is not a step of the linked process flow", Errors(Row(again, 5)));
        Assert.Equal(2, again.Int("duplicates"));

        // Replace mode: existing lines are not duplicates; the revision ends with exactly the file's valid lines.
        var replace = await ImportOkAsync(w.Eng, "pfmea", id, $"{Header}\n10,Turn OD,OD oversize,Assembly interference,6,Tool wear,,3,,4,,,,,,\n10,Turn OD,Chatter,Noise,4,Insert loose,,2,,5,,,,,,", dryRun: false, new() { ["mode"] = "Replace" });
        Assert.Equal(0, replace.Int("duplicates"));
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}/lines")).GetArrayLength());

        // Excel with the customer's own column names (explicit mapping), a formula RPN column and a date cell.
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("FMEA");
        string[] head = ["Op No", "Function / Requirement", "Potential Failure Mode", "Effect", "SEV", "Cause", "OCC", "DET", "Risk", "Owner", "Target", "Action"];
        for (var i = 0; i < head.Length; i++) ws.Cell(1, i + 1).Value = head[i];
        ws.Cell(2, 1).Value = 10; ws.Cell(2, 2).Value = "Turn shoulder"; ws.Cell(2, 3).Value = "Shoulder length short"; ws.Cell(2, 4).Value = "Stack-up fails";
        ws.Cell(2, 5).Value = 8; ws.Cell(2, 6).Value = "Offset error"; ws.Cell(2, 7).Value = 3; ws.Cell(2, 8).Value = 5; ws.Cell(2, 9).FormulaA1 = "E2*G2*H2";
        ws.Cell(2, 10).Value = w.Email; ws.Cell(2, 11).Value = new DateTime(2027, 1, 15); ws.Cell(2, 12).Value = "Probe shoulder in cycle";
        wb.RecalculateAllFormulas();
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        var map = JsonSerializer.Serialize(new Dictionary<string, string> { ["stepSequence"] = "Op No", ["processFunction"] = "Function / Requirement", ["failureEffect"] = "Effect", ["rpn"] = "Risk", ["responsible"] = "Owner", ["dueOn"] = "Target", ["recommendedAction"] = "Action" });
        var r = await UploadAsync(w.Eng, $"/api/apqp/pfmea/{id}/import", ms.ToArray(), "customer-fmea.xlsx", new() { ["dryRun"] = "false", ["mapping"] = map });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var x = await r.JsonAsync();
        Assert.Equal("Committed", x.Str("status")); Assert.Equal(1, x.Int("accepted"));
        Assert.Empty(Row(x, 2).GetProperty("warnings").EnumerateArray()); // formula RPN 120 matches the calculated value
        var line = (await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}/lines")).EnumerateArray().Single(l => l.Str("failureMode") == "Shoulder length short");
        Assert.Equal(120, line.Int("rpn")); Assert.Equal("2027-01-15", line.Str("dueOn"));

        // Missing required columns: nothing is validated or written.
        var miss = await ImportOkAsync(w.Eng, "pfmea", id, "Step,Failure mode\n10,Crack", dryRun: true);
        Assert.Equal("Refused", miss.Str("status"));
        Assert.Contains("Process function", miss.GetProperty("missingRequired").ToString());
        // Unsupported formats are refused with a clear message.
        var bad = await UploadAsync(w.Eng, $"/api/apqp/pfmea/{id}/import", [1, 2, 3], "old.xls", new() { ["dryRun"] = "true" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("save the file as .xlsx", await bad.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Released_revisions_never_change_new_revision_is_created_atomically_and_unauthorized_users_are_refused()
    {
        var w = await SetupAsync(app);
        var id = await PfmeaAsync(w, "Valve PFMEA");
        await ImportOkAsync(w.Eng, "pfmea", id, $"{Header}\n10,Bore,Bore oversize,Leak,5,Tool wear,,3,,3,,,,,,", dryRun: false);
        await ReleaseAsync(w, "pfmea", id);
        var good = $"{Header}\n20,Hone,Finish rough,Seal wear,5,Stone glazed,,3,,3,,,,,,";

        // Commit into the released revision is refused; dry run explains.
        var r = await UploadAsync(w.Eng, $"/api/apqp/pfmea/{id}/import", Encoding.UTF8.GetBytes(good), "a.csv", new() { ["dryRun"] = "false" });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Contains("released revisions cannot change", await r.Content.ReadAsStringAsync());
        Assert.Contains("Released revisions never change", (await ImportOkAsync(w.Eng, "pfmea", id, good, dryRun: true)).Str("message"));
        // createRevision needs a change reason.
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(w.Eng, $"/api/apqp/pfmea/{id}/import", Encoding.UTF8.GetBytes(good), "a.csv", new() { ["dryRun"] = "false", ["createRevision"] = "true" })).StatusCode);

        // Rollback: a refused commit with createRevision creates no revision and writes nothing.
        var bad = $"{good}\n30,Wash,,Contamination,5,Filter blocked,,3,,3,,,,,,";
        var refused = await ImportOkAsync(w.Eng, "pfmea", id, bad, dryRun: false, new() { ["createRevision"] = "true", ["revisionReason"] = "Add honing" });
        Assert.Equal("Refused", refused.Str("status"));
        Assert.Equal(1, (await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}/history")).GetArrayLength());

        var ok = await ImportOkAsync(w.Eng, "pfmea", id, good, dryRun: false, new() { ["createRevision"] = "true", ["revisionReason"] = "Add honing step" });
        Assert.Equal("Committed", ok.Str("status")); Assert.True(ok.GetProperty("revisionCreated").GetBoolean());
        Assert.Equal(2, ok.Int("documentRevision"));
        var rev2 = ok.Id("documentId");
        Assert.NotEqual(id, rev2);
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/apqp/pfmea/{rev2}/lines")).GetArrayLength()); // copied line + imported line
        Assert.Equal(1, (await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}/lines")).GetArrayLength());   // released revision unchanged
        Assert.Equal(2, (await w.Eng.GetElAsync($"/api/apqp/pfmea/{id}")).GetProperty("document").Int("status"));
        Assert.Equal(0, (await w.Eng.GetElAsync($"/api/apqp/pfmea/{rev2}")).GetProperty("document").Int("status"));
        // A second createRevision is refused while the draft is open (one open revision per document).
        Assert.Equal(HttpStatusCode.Conflict, (await UploadAsync(w.Eng, $"/api/apqp/pfmea/{id}/import", Encoding.UTF8.GetBytes(good), "a.csv", new() { ["dryRun"] = "false", ["createRevision"] = "true", ["revisionReason"] = "x" })).StatusCode);
        // History spans both revisions.
        Assert.Contains((await w.Eng.GetElAsync($"/api/apqp/pfmea/{rev2}/imports")).EnumerateArray(), h => h.Int("documentRevision") == 1);

        // Unauthorized: view-only at the site, edit at another site, no APQP permission.
        var (_, viewer) = await app.CreateUserAsync([Permissions.ApqpView], w.Site);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(viewer, $"/api/apqp/pfmea/{rev2}/import", Encoding.UTF8.GetBytes(good), "a.csv", new() { ["dryRun"] = "true" })).StatusCode);
        var other = await app.CreateSiteAsync();
        var (_, elsewhere) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpEdit], other);
        var e = await UploadAsync(elsewhere, $"/api/apqp/pfmea/{rev2}/import", Encoding.UTF8.GetBytes(good), "a.csv", new() { ["dryRun"] = "false" });
        Assert.True(e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound, e.StatusCode.ToString());
        Assert.Equal(1, (await w.Eng.GetElAsync($"/api/apqp/pfmea/{rev2}/lines")).EnumerateArray().Count(l => l.Str("failureMode") == "Finish rough"));
    }
}
