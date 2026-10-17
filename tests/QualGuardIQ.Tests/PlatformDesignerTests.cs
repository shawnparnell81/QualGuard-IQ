using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Prompt 8 designers on the shared engines: workflow designer (parallel, conditional, escalation), form designer, business rules, data governance, recovery, marketplace, video content.</summary>
[Collection("api")]
public sealed class PlatformDesignerTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    private static async Task<Guid> DispositionedNcrAsync(ApiClient c, Guid site, int severity)
    {
        var id = (await (await c.PostOkAsync("/api/ncrs", new { title = "WF NCR " + Guid.NewGuid().ToString("N")[..6], description = "Burr", source = "In-process", siteId = site, detectedOn = Today, severity })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/ncrs/{id}/containment", new { text = "Quarantined" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync($"/api/ncrs/{id}/disposition", new { disposition = 1, justification = "Within cosmetic limits", capaRequired = false })).StatusCode);
        return id;
    }

    [Fact]
    public async Task Workflow_designer_runs_parallel_and_conditional_steps_on_the_approval_engine()
    {
        var site = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        object Step(int order, int stage, string name, string perm, string? field = null, string? op = null, string? value = null, int? esc = null) =>
            new { order, stage, name, requiredPermission = perm, signatureMeaning = "Approved", conditionField = field, conditionOperator = op, conditionValue = value, escalateAfterHours = esc };
        // Every step conditional, or an unknown field, is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/config/approval-routes/Ncr?siteId={site}", new { name = "Bad", preventSelfApproval = true, steps = new[] { Step(1, 1, "Only critical", Permissions.NcrApprove, "Severity", "equals", "Critical") } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/config/approval-routes/Ncr?siteId={site}", new { name = "Bad", preventSelfApproval = true, steps = new[] { Step(1, 1, "Q", Permissions.NcrApprove), Step(2, 2, "X", Permissions.NcrApprove, "NoSuchField", "equals", "1") } })).StatusCode);
        var route = new { name = "Designed NCR disposition", preventSelfApproval = true, steps = new[] {
            Step(1, 1, "Quality review", Permissions.NcrApprove, esc: 24),
            Step(2, 2, "Production sign-off", Permissions.NcrApprove), Step(3, 2, "Engineering sign-off", Permissions.CapaApprove),
            Step(4, 3, "Customer quality", Permissions.NcrApprove, "Severity", "equals", "Critical") } };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/admin/config/approval-routes/Ncr?siteId={site}", route)).StatusCode);
        var fields = await admin.GetElAsync("/api/admin/config/workflow-designer/fields/Ncr");
        Assert.Contains(fields.GetProperty("fields").EnumerateArray(), f => f.Str("name") == "Severity");

        var (_, author) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, q1) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrApprove], site);
        var (_, q2) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrApprove], site);
        var (_, eng) = await app.CreateUserAsync([Permissions.NcrView, Permissions.CapaApprove], site);
        var ncr = await DispositionedNcrAsync(author, site, 1);
        var number = (await author.GetElAsync($"/api/ncrs/{ncr}")).Str("number");

        var preview = await admin.GetElAsync($"/api/admin/config/approval-routes/Ncr/preview/{ncr}?siteId={site}");
        Assert.Contains(preview.EnumerateArray(), p => p.Str("step") == "Customer quality" && !p.GetProperty("applies").GetBoolean()); // Major: branch skipped

        Assert.DoesNotContain((await eng.GetJsonAsync<MyWork>("/api/my/work")).Approvals, a => a.RecordNumber == number); // stage 2 not reached yet
        Assert.Equal(HttpStatusCode.OK, (await q1.SignAsync(await q1.PendingApprovalIdAsync(number))).StatusCode);
        // Stage 2 runs in parallel: both groups see it.
        var approval = await q2.PendingApprovalIdAsync(number);
        Assert.Equal(approval, await eng.PendingApprovalIdAsync(number));
        Assert.Equal(HttpStatusCode.OK, (await q2.SignAsync(approval)).StatusCode);
        Assert.Equal(2, (await author.GetElAsync($"/api/ncrs/{ncr}")).Int("status")); // still waiting for engineering
        Assert.DoesNotContain((await q1.GetJsonAsync<MyWork>("/api/my/work")).Approvals, a => a.RecordNumber == number); // q1 signed already
        Assert.Equal(HttpStatusCode.OK, (await eng.SignAsync(approval)).StatusCode);
        Assert.Equal(3, (await author.GetElAsync($"/api/ncrs/{ncr}")).Int("status")); // conditional stage skipped

        // A critical NCR takes the customer-quality branch.
        var crit = await DispositionedNcrAsync(author, site, 2);
        var critNo = (await author.GetElAsync($"/api/ncrs/{crit}")).Str("number");
        await q1.SignAsync(await q1.PendingApprovalIdAsync(critNo));
        var a2 = await q2.PendingApprovalIdAsync(critNo);
        await q2.SignAsync(a2); await eng.SignAsync(a2);
        Assert.Equal(2, (await author.GetElAsync($"/api/ncrs/{crit}")).Int("status"));
        var (_, q3) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrApprove], site);
        Assert.Equal(HttpStatusCode.OK, (await q3.SignAsync(await q3.PendingApprovalIdAsync(critNo))).StatusCode);
        Assert.Equal(3, (await author.GetElAsync($"/api/ncrs/{crit}")).Int("status"));
        await admin.DeleteAsync($"/api/admin/config/approval-routes/Ncr/sites/{site}");
    }

    static readonly string DynamicForm = JsonSerializer.Serialize(new object[] {
        new { key = "result", label = "Result", type = "choice", options = new[] { "Pass", "Fail" }, required = true },
        new { key = "reason", label = "Failure reason", type = "text", visibleWhen = new { field = "result", @operator = "equals", value = "Fail" }, requiredWhen = new { field = "result", @operator = "equals", value = "Fail" } },
        new { key = "start", label = "Start", type = "date" },
        new { key = "end", label = "End", type = "date", checks = new[] { new { @operator = "greaterOrEqual", field = "start", message = "End must be on or after start." } } },
        new { key = "serial", label = "Serial", type = "text", pattern = "^SN-[0-9]{4}$", patternMessage = "Serial must look like SN-1234." },
        new { key = "d1", label = "D1", type = "number", checks = new[] { new { @operator = "lessOrEqual", value = "10", message = "D1 must be 10 or less." } } } });

    [Fact]
    public async Task Form_designer_applies_dynamic_rules_and_validation_and_versions_library_components()
    {
        var site = await app.CreateSiteAsync();
        var (_, designer) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsApprove], site);
        async Task<JsonElement> Preview(object values) => await (await designer.PostOkAsync("/api/forms/library/preview", new { fieldsJson = DynamicForm, values })).JsonAsync();

        var pass = await Preview(new { result = "Pass", reason = "left over", serial = "SN-0001" });
        Assert.True(pass.GetProperty("error").ValueKind == JsonValueKind.Null, pass.GetRawText());
        Assert.DoesNotContain(pass.GetProperty("visible").EnumerateArray(), v => v.GetString() == "reason"); // hidden
        Assert.Equal(JsonValueKind.Null, pass.GetProperty("values").GetProperty("reason").ValueKind); // hidden values are cleared
        var fail = await Preview(new { result = "Fail" });
        Assert.Contains("Failure reason is required", fail.Str("error"));
        var bad = await Preview(new { result = "Pass", start = "2026-10-10", end = "2026-10-01", serial = "123", d1 = "12" });
        Assert.Contains("End must be on or after start.", bad.Str("error"));
        Assert.Contains("Serial must look like SN-1234.", bad.Str("error"));
        Assert.Contains("D1 must be 10 or less.", bad.Str("error"));
        var broken = await (await designer.PostOkAsync("/api/forms/library/preview", new { fieldsJson = "[{\"key\":\"a\",\"label\":\"A\",\"type\":\"text\",\"visibleWhen\":{\"field\":\"zzz\",\"operator\":\"equals\",\"value\":\"1\"}}]", values = new { } })).JsonAsync();
        Assert.False(broken.GetProperty("valid").GetBoolean());

        var key = "lot" + Guid.NewGuid().ToString("N")[..6];
        var comp = (await (await designer.PostOkAsync("/api/forms/library", new { key, name = "Lot identification", category = "Traceability", description = (string?)null,
            fieldsJson = "[{\"key\":\"lot\",\"label\":\"Lot\",\"type\":\"text\",\"required\":true},{\"key\":\"qty\",\"label\":\"Qty\",\"type\":\"number\"},{\"key\":\"half\",\"label\":\"Half\",\"type\":\"calc\",\"formula\":\"qty / 2\"}]" })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NotFound, (await designer.GetAsync($"/api/forms/library/{comp}/insert")).StatusCode); // drafts cannot be inserted
        Assert.Equal(HttpStatusCode.Forbidden, (await designer.PostAsync($"/api/forms/library/{comp}/publish")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/forms/library/{comp}/publish")).StatusCode);
        var ins = await designer.GetElAsync($"/api/forms/library/{comp}/insert?prefix=incoming");
        Assert.Contains(ins.EnumerateArray(), f => f.Str("key") == "incoming_lot" && f.Str("fromComponent") == $"{key}@1");
        Assert.Contains(ins.EnumerateArray(), f => f.Str("key") == "incoming_half" && f.Str("formula") == "incoming_qty / 2");
        var v2 = (await (await designer.PostOkAsync($"/api/forms/library/{comp}/new-version")).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.Conflict, (await designer.PutAsync($"/api/forms/library/{comp}", new { key, name = "Changed", category = "x", description = "", fieldsJson = "[{\"key\":\"a\",\"label\":\"A\",\"type\":\"text\"}]" })).StatusCode); // published is frozen
        Assert.Equal(HttpStatusCode.NoContent, (await approver.PostAsync($"/api/forms/library/{v2}/publish")).StatusCode);
        var list = await designer.GetElAsync("/api/forms/library");
        Assert.DoesNotContain(list.EnumerateArray(), c => c.Id() == comp); // v1 retired
        Assert.Contains(list.EnumerateArray(), c => c.Id() == v2 && c.Int("version") == 2);
    }

    [Fact]
    public async Task Business_rules_validate_route_and_automate_without_code()
    {
        var site = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.GetAsync("/api/platform/rules")).StatusCode);
        object Rule(string name, int kind, object[] conds, string message, string? target = null, string? trigger = null, int? due = null) =>
            new { name, recordType = "Ncr", kind, trigger, conditions = conds, message, targetPermission = target, dueDays = due, afterHours = (int?)null, siteId = site, priority = 100, enabled = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/rules", Rule("Bad", 0, [new { field = "Nope", @operator = "equals", value = "x" }], "x"))).StatusCode);
        var block = (await (await admin.PostOkAsync("/api/platform/rules", Rule("Critical needs CAPA", 0, [new { field = "Severity", @operator = "equals", value = "Critical" }, new { field = "CapaRequired", @operator = "equals", value = "false" }], "Critical NCRs need a CAPA."))).JsonAsync()).Id();
        var route = (await (await admin.PostOkAsync("/api/platform/rules", Rule("Supplier NCRs to supplier quality", 1, [new { field = "Source", @operator = "equals", value = "Supplier" }], "Supplier issue to review", Permissions.SuppliersEdit, due: 3))).JsonAsync()).Id();
        var auto = (await (await admin.PostOkAsync("/api/platform/rules", Rule("Alert on critical NCR", 3, [new { field = "Severity", @operator = "greaterOrEqual", value = "Critical" }], "Critical NCR raised", Permissions.NcrApprove, PlatformEventsCreated))).JsonAsync()).Id();

        async Task<Guid> Ncr(int severity, string source)
        {
            var id = (await (await author.PostOkAsync("/api/ncrs", new { title = "Rule NCR", description = "x", source, siteId = site, detectedOn = Today, severity })).JsonAsync()).Id();
            await author.PostAsync($"/api/ncrs/{id}/containment", new { text = "Held" });
            return id;
        }
        var crit = await Ncr(2, "In-process");
        var blocked = await author.PostAsync($"/api/ncrs/{crit}/disposition", new { disposition = 1, justification = "Use as is", capaRequired = false });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains("Critical NCRs need a CAPA.", await blocked.Content.ReadAsStringAsync());
        var test = await admin.GetElAsync($"/api/platform/rules/{block}/test/{crit}");
        Assert.True(test.GetProperty("matches").GetBoolean()); // the blocked submission rolled back, so the stored record still matches
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/ncrs/{crit}/disposition", new { disposition = 1, justification = "Use as is", capaRequired = true })).StatusCode);

        var sup = await Ncr(0, "Supplier");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/ncrs/{sup}/disposition", new { disposition = 1, justification = "Use as is", capaRequired = false })).StatusCode);
        var routed = await admin.GetElAsync($"/api/platform/rules/firings?ruleId={route}");
        Assert.Contains(routed.EnumerateArray(), f => f.Id("recordId") == sup);
        Assert.DoesNotContain(routed.EnumerateArray(), f => f.Id("recordId") == crit);

        await admin.PostAsync("/api/platform/events/pump");
        var fired = await admin.GetElAsync($"/api/platform/rules/firings?ruleId={auto}");
        Assert.Contains(fired.EnumerateArray(), f => f.Id("recordId") == crit);
        Assert.DoesNotContain(fired.EnumerateArray(), f => f.Id("recordId") == sup);
        foreach (var id in new[] { block, route, auto }) await admin.DeleteAsync($"/api/platform/rules/{id}");
    }

    const string PlatformEventsCreated = "record.created";

    [Fact]
    public async Task Data_governance_tracks_quality_and_applies_master_changes_only_after_approval()
    {
        var site = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        var (ed1, editor) = await app.CreateUserAsync([Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var (_, steward) = await app.CreateUserAsync([Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PutAsync("/api/governance/data/domains/Part", new { stewardId = ed1, ownerId = (Guid?)null, classification = 2, description = "Parts" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/governance/data/domains/Part", new { stewardId = ed1, ownerId = (Guid?)null, classification = 2, description = "Part master" })).StatusCode);
        var r1 = (await (await admin.PostOkAsync("/api/governance/data/rules", new { domainKey = "Part", field = "Material", ruleType = 0, parameter = (string?)null, blocking = false })).JsonAsync()).Id();
        var r2 = (await (await admin.PostOkAsync("/api/governance/data/rules", new { domainKey = "Part", field = "UnitOfMeasure", ruleType = 3, parameter = "EA,KG,M,L", blocking = true })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/governance/data/rules", new { domainKey = "Part", field = "Nope", ruleType = 0, parameter = (string?)null, blocking = false })).StatusCode);

        var pn = "GOV-" + Guid.NewGuid().ToString("N")[..6];
        var part = (await (await editor.PostOkAsync("/api/master/parts", new { partNumber = pn, name = "Bracket", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();
        var q = await admin.GetElAsync("/api/governance/data/quality/Part");
        Assert.Contains(q.GetProperty("sample").EnumerateArray(), v => v.Id("recordId") == part && v.Str("field") == "Material");
        Assert.True(q.GetProperty("compliance").GetDecimal() < 100m);
        await admin.PostOkAsync("/api/governance/data/quality/run");
        var overview = await editor.GetElAsync("/api/governance/data/domains");
        var partDomain = overview.EnumerateArray().First(d => d.Str("key") == "Part");
        Assert.Equal(2, partDomain.Int("classification"));
        Assert.NotEqual(JsonValueKind.Null, partDomain.GetProperty("latest").ValueKind);

        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsync("/api/governance/data/changes", new { domainKey = "Part", recordId = part, changes = new Dictionary<string, string?> { ["UnitOfMeasure"] = "BOX" }, reason = "Pack change" })).StatusCode); // blocking rule
        Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsync("/api/governance/data/changes", new { domainKey = "Part", recordId = part, changes = new Dictionary<string, string?> { ["SiteId"] = Guid.NewGuid().ToString() }, reason = "Move" })).StatusCode); // not governed
        var change = await (await editor.PostOkAsync("/api/governance/data/changes", new { domainKey = "Part", recordId = part, changes = new Dictionary<string, string?> { ["Material"] = "6061-T6 aluminium" }, reason = "Material missing" })).JsonAsync();
        Assert.Null((await editor.GetElAsync($"/api/master/parts/{part}")).GetProperty("material").GetString()); // not applied before approval
        Assert.DoesNotContain((await editor.GetJsonAsync<MyWork>("/api/my/work")).Approvals, a => a.RecordNumber == change.Str("number")); // no self-approval
        Assert.Equal(HttpStatusCode.OK, (await steward.SignAsync(await steward.PendingApprovalIdAsync(change.Str("number")))).StatusCode);
        Assert.Equal("6061-T6 aluminium", (await editor.GetElAsync($"/api/master/parts/{part}")).Str("material"));
        var changes = await editor.GetElAsync("/api/governance/data/changes?domain=Part");
        Assert.Contains(changes.EnumerateArray(), c => c.Id() == change.Id() && c.Int("status") == 2);
        await admin.DeleteAsync($"/api/governance/data/rules/{r1}"); await admin.DeleteAsync($"/api/governance/data/rules/{r2}");
    }

    [Fact]
    public async Task Recovery_center_tracks_backups_tests_and_procedures_and_reports_recoverability()
    {
        var admin = await app.AdminAsync();
        var site = await app.CreateSiteAsync();
        var (_, user) = await app.CreateUserAsync([Permissions.MasterDataView], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/platform/dr/status")).StatusCode);
        var fp = await admin.GetElAsync("/api/platform/dr/fingerprint");
        Assert.True(fp.GetProperty("counts").GetProperty("audit_entries").GetInt64() > 0);
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/dr/backups", new { kind = 0, label = "bad", location = "/x", method = "pg_dump", startedAt = now, completedAt = now.AddMinutes(-5), sizeBytes = 1, sha256 = (string?)null, state = 0 })).StatusCode);
        var dbBackup = (await (await admin.PostOkAsync("/api/platform/dr/backups", new { kind = 0, label = "nightly-db", location = "/backups/qgiq/database.dump", method = "pg_dump custom format", startedAt = now.AddMinutes(-3), completedAt = now.AddMinutes(-2), sizeBytes = 1048576, sha256 = new string('a', 64), state = 0, fingerprint = fp })).JsonAsync()).Id();
        await admin.PostOkAsync("/api/platform/dr/backups", new { kind = 1, label = "nightly-files", location = "/backups/qgiq/files", method = "mc mirror", startedAt = now.AddMinutes(-3), completedAt = now.AddMinutes(-1), sizeBytes = 2048, sha256 = (string?)null, state = 0 });
        var st = await admin.GetElAsync("/api/platform/dr/status");
        Assert.False(st.GetProperty("recoverable").GetBoolean());
        Assert.Contains(st.GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("test restore"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/platform/dr/backups/{dbBackup}/verify", new { passed = true, notes = "verify-backup.sh PASS: checksum, migration and row counts match" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/platform/dr/objectives", new { scenario = "Database server loss " + Guid.NewGuid().ToString("N")[..4], rtoMinutes = 240, rpoMinutes = 1440, procedureDocumentId = (Guid?)null })).StatusCode);
        st = await admin.GetElAsync("/api/platform/dr/status");
        Assert.DoesNotContain(st.GetProperty("gaps").EnumerateArray(), g => g.GetString()!.Contains("test restore"));
        Assert.Contains(st.GetProperty("objectives").EnumerateArray(), o => !o.GetProperty("met").GetBoolean() && o.GetProperty("issues").EnumerateArray().Any(i => i.GetString()!.Contains("procedure")));
        Assert.Equal(JsonValueKind.Object, st.GetProperty("lastVerifiedBackup").ValueKind);
    }

    static string License(string id, string[] extensions, DateTimeOffset expires, System.Security.Cryptography.ECDsa? signer = null)
    {
        static string B(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { licenseId = id, licensee = "Test Manufacturing", extensions, issuedAt = DateTimeOffset.UtcNow.AddDays(-1), expiresAt = expires });
        return $"QGL1.{B(payload)}.{B((signer ?? TestApp.LicenseSigner).SignData(payload, System.Security.Cryptography.HashAlgorithmName.SHA256))}";
    }

    [Fact]
    public async Task Marketplace_manages_dependencies_licenses_versions_and_installs_through_existing_engines()
    {
        var admin = await app.AdminAsync();
        var site = await app.CreateSiteAsync();
        var (_, user) = await app.CreateUserAsync([Permissions.MasterDataView], site);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/platform/marketplace")).StatusCode);
        foreach (var k in new[] { "aerospace-as9100", "medical-iso13485", "supplier-escalation", "part-master-hygiene" }) await admin.DeleteAsync($"/api/platform/marketplace/{k}");

        var catalog = await admin.GetElAsync("/api/platform/marketplace");
        var aero = catalog.EnumerateArray().First(p => p.GetProperty("key").GetString() == "aerospace-as9100");
        Assert.Equal("1.1.0", aero.GetProperty("latestVersion").GetString());
        Assert.True(aero.GetProperty("requiresLicense").GetBoolean());
        Assert.Contains(aero.GetProperty("dependencies").EnumerateArray(), d => d.GetProperty("key").GetString() == "module:Fai" && d.GetProperty("satisfied").GetBoolean());

        // dependency on another package, then license
        var noDep = await admin.PostAsync("/api/platform/marketplace/aerospace-as9100/install?version=1.0.0", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noDep.StatusCode);
        Assert.Contains("part-master-hygiene", await noDep.Content.ReadAsStringAsync());
        var hygiene = await (await admin.PostOkAsync("/api/platform/marketplace/part-master-hygiene/install", new { })).JsonAsync();
        var ruleIds = hygiene.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
        Assert.Equal(2, ruleIds.Count);
        Assert.Equal(2, (await admin.GetElAsync("/api/governance/data/rules")).EnumerateArray().Count(r => ruleIds.Contains(r.GetProperty("id").GetGuid())));
        var noLicense = await admin.PostAsync("/api/platform/marketplace/aerospace-as9100/install?version=1.0.0", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noLicense.StatusCode);
        Assert.Contains("license", await noLicense.Content.ReadAsStringAsync());

        // licenses: forged, expired, valid
        using var forger = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/licenses", new { licenseKey = License("L-FORGED", ["*"], DateTimeOffset.UtcNow.AddYears(1), forger) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/licenses", new { licenseKey = License("L-OLD", ["aerospace-as9100"], DateTimeOffset.UtcNow.AddDays(-1)) })).StatusCode);
        var lic = await (await admin.PostOkAsync("/api/platform/marketplace/licenses", new { licenseKey = License("L-" + Guid.NewGuid().ToString("N")[..8], ["aerospace-as9100"], DateTimeOffset.UtcNow.AddYears(1)) })).JsonAsync();

        // install 1.0.0: the rule engine and form library hold the content
        var inst = await (await admin.PostOkAsync("/api/platform/marketplace/aerospace-as9100/install?version=1.0.0", new { })).JsonAsync();
        Assert.Equal("1.0.0", inst.GetProperty("version").GetString());
        var rules = await admin.GetElAsync("/api/platform/rules");
        Assert.Contains(rules.EnumerateArray(), r => r.GetProperty("name").GetString() == "Critical NCR needs corrective action [aerospace-as9100]" && r.GetProperty("enabled").GetBoolean());
        Assert.Contains((await admin.GetElAsync("/api/forms/library")).EnumerateArray(), c => c.GetProperty("key").GetString() == "as9102_header" && c.GetProperty("status").GetInt32() == 0);
        aero = (await admin.GetElAsync("/api/platform/marketplace")).EnumerateArray().First(p => p.GetProperty("key").GetString() == "aerospace-as9100");
        Assert.True(aero.GetProperty("upgradeAvailable").GetBoolean());
        Assert.Equal("Licensed", aero.GetProperty("license").GetString());

        // dependents block removal; upgrade replaces content
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync("/api/platform/marketplace/part-master-hygiene")).StatusCode);
        var up = await (await admin.PostOkAsync("/api/platform/marketplace/aerospace-as9100/upgrade", new { })).JsonAsync();
        Assert.Equal("1.1.0", up.GetProperty("version").GetString());
        rules = await admin.GetElAsync("/api/platform/rules");
        Assert.Single(rules.EnumerateArray(), r => r.GetProperty("name").GetString()!.EndsWith("[aerospace-as9100]") && r.GetProperty("kind").GetInt32() == 0);
        Assert.Contains(rules.EnumerateArray(), r => r.GetProperty("name").GetString() == "Escalate critical NCR approvals [aerospace-as9100]");

        // disable switches the created rules off; revoked license blocks re-enabling
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/platform/marketplace/aerospace-as9100/enabled", new { enabled = false })).StatusCode);
        Assert.DoesNotContain((await admin.GetElAsync("/api/platform/rules")).EnumerateArray(), r => r.GetProperty("name").GetString()!.EndsWith("[aerospace-as9100]") && r.GetProperty("enabled").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/platform/marketplace/licenses/{lic.Id()}/revoke", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/platform/marketplace/aerospace-as9100/enabled", new { enabled = true })).StatusCode);

        // imported manifest = future packages without redesign; content is validated by the owning engine on install
        var key = "custom-" + Guid.NewGuid().ToString("N")[..6];
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/platform/marketplace/packages", new { key, name = "Bad custom", kind = 0, publisher = "Acme", version = "1.0.0", description = "x", dependsOn = new[] { "module:Quality" }, requiresLicense = false, content = new { dataRules = new[] { new { domainKey = "Part", field = "Nope", ruleType = 0, parameter = (string?)null, blocking = false } } } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/platform/marketplace/packages", new { key, name = "Again", kind = 0, publisher = "Acme", version = "1.0.0", description = "x", requiresLicense = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/platform/marketplace/{key}/install", new { })).StatusCode);
        Assert.DoesNotContain((await admin.GetElAsync("/api/platform/marketplace")).EnumerateArray(), p => p.GetProperty("key").GetString() == key && p.GetProperty("installed").ValueKind == JsonValueKind.Object);

        // uninstall removes the created configuration; draft component is deleted
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/platform/marketplace/aerospace-as9100")).StatusCode);
        Assert.DoesNotContain((await admin.GetElAsync("/api/platform/rules")).EnumerateArray(), r => r.GetProperty("name").GetString()!.EndsWith("[aerospace-as9100]"));
        Assert.DoesNotContain((await admin.GetElAsync("/api/forms/library")).EnumerateArray(), c => c.GetProperty("key").GetString() == "as9102_header");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/platform/marketplace/part-master-hygiene")).StatusCode);
    }

    static async Task<HttpResponseMessage> UploadFileAsync(ApiClient c, string type, Guid id, string name, byte[] bytes, int classification = 0, Guid? replaces = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", name); form.Add(new StringContent(classification.ToString()), "classification"); form.Add(new StringContent("Video"), "category");
        if (replaces is not null) form.Add(new StringContent(replaces.ToString()!), "replacesId");
        return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/records/{type}/{id}/files") { Content = form });
    }

    [Fact]
    public async Task Content_library_handles_video_with_versions_ranges_retention_and_classification()
    {
        var admin = await app.AdminAsync();
        var site = await app.CreateSiteAsync();
        var (_, editor) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.NcrView], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.NcrView], await app.CreateSiteAsync());
        var ncr = (await (await editor.PostOkAsync("/api/ncrs", new { title = "Video NCR " + Guid.NewGuid().ToString("N")[..6], description = "Setup issue on line 3", source = "In-process", siteId = site, detectedOn = Today, severity = 1 })).JsonAsync()).Id();
        var bytes = Enumerable.Range(0, 70_000).Select(i => (byte)(i % 251)).ToArray();

        var v1 = await (await UploadFileAsync(editor, "Ncr", ncr, "line3-setup.mp4", bytes)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("video/mp4", v1.GetProperty("contentType").GetString());
        Assert.Equal("Video", v1.GetProperty("kind").GetString());
        var v2r = await UploadFileAsync(editor, "Ncr", ncr, "line3-setup.mp4", bytes.Reverse().ToArray(), replaces: v1.Id());
        Assert.Equal(HttpStatusCode.OK, v2r.StatusCode);
        var v2 = await v2r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, v2.GetProperty("version").GetInt32());
        Assert.Equal(2, (await viewer.GetElAsync($"/api/files/{v2.Id()}/versions")).GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadFileAsync(editor, "Ncr", ncr, "clip.avi", bytes)).StatusCode);

        // byte-range playback
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{v2.Id()}?inline=true"); req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 109);
        var part = await viewer.Http.SendAsync(req);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal("video/mp4", part.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes.Reverse().Skip(100).Take(10).ToArray(), await part.Content.ReadAsByteArrayAsync());

        // library: current versions only, only records the caller can view
        var lib = await viewer.GetElAsync("/api/files?kind=Video");
        Assert.Contains(lib.EnumerateArray(), f => f.Id() == v2.Id() && f.GetProperty("versions").GetInt32() == 2 && f.GetProperty("ownerType").GetString() == "Ncr");
        Assert.DoesNotContain(lib.EnumerateArray(), f => f.Id() == v1.Id());
        Assert.DoesNotContain((await outsider.GetElAsync("/api/files?kind=Video")).EnumerateArray(), f => f.Id() == v2.Id());

        // restricted: edit permission needed to open, and the access is logged
        var restricted = await (await UploadFileAsync(editor, "Ncr", ncr, "operator-face.mp4", bytes, classification: 2)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync($"/api/files/{restricted.Id()}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync($"/api/files/{restricted.Id()}")).StatusCode);
        var events = await admin.GetElAsync("/api/security-events?pageSize=50");
        Assert.Contains(events.GetProperty("items").EnumerateArray(), e => e.GetProperty("eventType").GetString() == "FileOpened" && e.GetProperty("detail").GetString()!.Contains(restricted.Id().ToString()));

        // retention: policy applies on upload, blocks removal, can only be extended, by governance.edit
        var policy = (await (await admin.PostOkAsync("/api/governance/retention/policies", new { name = "NCR evidence " + Guid.NewGuid().ToString("N")[..4], recordType = "Ncr", productFamily = (string?)null, regulatoryCategory = (string?)null, retainYears = 10, action = 0 })).JsonAsync()).Id();
        try
        {
            var kept = await (await UploadFileAsync(editor, "Ncr", ncr, "teardown.webm", bytes)).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("video/webm", kept.GetProperty("contentType").GetString());
            var until = DateOnly.Parse(kept.GetProperty("retainUntil").GetString()!);
            Assert.True(until >= DateOnly.FromDateTime(DateTime.UtcNow).AddYears(10).AddDays(-2));
            Assert.Equal(HttpStatusCode.BadRequest, (await editor.PostAsync($"/api/files/{kept.Id()}/archive")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/files/{kept.Id()}/retention", new { retainUntil = until.AddYears(2), reason = "Customer contract" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/files/{kept.Id()}/retention", new { retainUntil = until.AddYears(-5), reason = "Shorter" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/files/{kept.Id()}/retention", new { retainUntil = until.AddYears(2), reason = "Customer contract requires 12 years" })).StatusCode);
            var next = await (await UploadFileAsync(editor, "Ncr", ncr, "teardown.webm", bytes.Reverse().ToArray(), replaces: kept.Id())).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(until.AddYears(2), DateOnly.Parse(next.GetProperty("retainUntil").GetString()!)); // new version keeps the longer retention
        }
        finally
        {
            await admin.PutAsync($"/api/governance/retention/policies/{policy}", new { name = "NCR evidence (off)", recordType = "Ncr", productFamily = (string?)null, regulatoryCategory = (string?)null, retainYears = 10, action = 0, isActive = false });
        }
        Assert.Equal(HttpStatusCode.NoContent, (await editor.PostAsync($"/api/files/{v2.Id()}/archive")).StatusCode);
    }
}
