using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Sequfloh;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class SequflohInstructionTests(TestApp app)
{
    static async Task<JsonElement> PostEl(ApiClient c, string url, object body) => await (await c.PostOkAsync(url, body)).JsonAsync();
    static async Task PutOk(ApiClient c, string url, object body)
    {
        var r = await c.PutAsync(url, body);
        if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"PUT {url} failed: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }
    static readonly string[] AuthorPerms = [Permissions.SequflohAccess, Permissions.SequflohCreateWorkInstruction, Permissions.SequflohEditDraftWorkInstruction, Permissions.SequflohSubmitWorkInstruction];
    static readonly string[] ApproverPerms = [Permissions.SequflohAccess, Permissions.SequflohReviewWorkInstruction, Permissions.SequflohApproveWorkInstruction, Permissions.SequflohReleaseWorkInstruction,
        Permissions.SequflohReviseWorkInstruction, Permissions.SequflohObsoleteWorkInstruction, Permissions.SequflohArchiveWorkInstruction, Permissions.SequflohRestoreWorkInstruction,
        Permissions.SequflohEditDraftWorkInstruction, Permissions.SequflohSubmitWorkInstruction];

    static object Save(JsonElement wi, Guid site, object? content = null, object[]? steps = null, object[]? chars = null, object[]? parts = null, string? effective = null, Guid? partId = null,
        string? repairDescription = null, uint rowVersion = 0) => new
    {
        title = wi.GetProperty("instruction").Str("title"), siteId = site, departmentId = (Guid?)null, ownerId = (Guid?)null, reviewerId = (Guid?)null, approverId = (Guid?)null,
        partId = partId ?? (wi.GetProperty("instruction").GetProperty("partId").ValueKind == JsonValueKind.Null ? (Guid?)null : wi.GetProperty("instruction").GetProperty("partId").GetGuid()),
        repairDescription, effectiveDate = effective, content = content ?? new { }, steps = steps ?? [], characteristics = chars ?? [], partsRows = parts ?? [], rowVersion
    };

    [Fact]
    public async Task Five_instruction_types_open_their_own_designer_and_numbering()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(AuthorPerms, site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.SequflohAccess], site);
        var schema = (await author.GetElAsync("/api/sequfloh/instructions/schema")).EnumerateArray().ToList();
        Assert.Equal(5, schema.Count);
        Assert.Contains(schema, s => s.Str("type") == "RepairWork" && s.GetProperty("hasPartsRows").GetBoolean());
        Assert.Contains(schema, s => s.Str("type") == "Fai" && s.GetProperty("hasCharacteristics").GetBoolean());
        var prefixes = new Dictionary<string, string> { ["Validation"] = "VWI-", ["ValidationFai"] = "WI-", ["Fai"] = "FWI-", ["RepairTest"] = "RTI-", ["RepairWork"] = "RWI-" };
        foreach (var (type, prefix) in prefixes)
        {
            var c = await PostEl(author, "/api/sequfloh/instructions", new { type, title = $"{type} instruction", siteId = site });
            Assert.StartsWith(prefix, c.Str("number"));
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/sequfloh/instructions", new { type = "Fai", title = "x", siteId = site })).StatusCode);
        var repairFolder = await viewer.GetElAsync("/api/sequfloh/instructions?folder=repair&pageSize=100");
        Assert.All(repairFolder.GetProperty("items").EnumerateArray(), i => Assert.StartsWith("Repair", i.Str("type")));
    }

    [Fact]
    public async Task Lifecycle_is_enforced_recorded_and_released_records_change_only_by_revision()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(AuthorPerms, site);
        var (_, approver) = await app.CreateUserAsync(ApproverPerms, site);
        var id = (await PostEl(author, "/api/sequfloh/instructions", new { type = "Validation", title = "Seal leak validation", siteId = site })).Id();
        var url = $"/api/sequfloh/instructions/{id}";
        var wi = await author.GetElAsync(url);
        Assert.Equal("Draft", wi.GetProperty("instruction").Str("status"));
        // submit with no steps is blocked
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"{url}/actions/submit-review", new { })).StatusCode);
        // limits are validated, sequence must ascend
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync(url, Save(wi, site, steps: [new { sequence = 10, title = "Pressurize", lowerLimit = 9, upperLimit = 2 }]))).StatusCode);
        var content = new { validationAcceptance = new { overallAcceptanceCriteria = "No leak at 150 psi for 10 minutes" }, purpose = new { purpose = "Prove seal integrity", bogus = "dropped" } };
        await PutOk(author, url, Save(wi, site, content, [new { sequence = 20, title = "Leak check" }, new { sequence = 10, title = "Pressurize", target = 150, lowerLimit = 145, upperLimit = 155, unit = "psi" }]));
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"{url}/actions/submit-review", new { })).StatusCode); // out of order
        wi = await author.GetElAsync(url);
        Assert.False(wi.GetProperty("instruction").GetProperty("content").GetProperty("purpose").TryGetProperty("bogus", out _));
        await PutOk(author, url, Save(wi, site, content, [new { sequence = 10, title = "Pressurize", target = 150, lowerLimit = 145, upperLimit = 155, unit = "psi" }, new { sequence = 20, title = "Leak check" }]));
        await author.PostOkAsync($"{url}/actions/submit-review", new { });
        Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync($"{url}/actions/submit-review", new { })).StatusCode); // duplicate transition
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsync(url, Save(wi, site, content))).StatusCode); // not editable in review
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"{url}/actions/return", new { comment = "" })).StatusCode);
        await approver.PostOkAsync($"{url}/actions/return", new { comment = "Add the hold time unit" });
        await author.PostOkAsync($"{url}/actions/submit-review", new { });
        await approver.PostOkAsync($"{url}/actions/submit-approval", new { });
        Assert.Equal(HttpStatusCode.Forbidden, (await author.PostAsync($"{url}/actions/approve", new { })).StatusCode);
        await approver.PostOkAsync($"{url}/actions/approve", new { });
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"{url}/actions/release", new { })).StatusCode); // effective date required
        await approver.PostOkAsync($"{url}/actions/release", new { effectiveDate = "2026-11-01" });
        wi = await author.GetElAsync(url);
        Assert.Equal("Released", wi.GetProperty("instruction").Str("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsync(url, Save(wi, site, content))).StatusCode); // released is read-only
        var actions = wi.GetProperty("transitions").EnumerateArray().Select(t => t.Str("action")).ToList();
        Assert.Equal(["create", "submit-review", "return", "submit-review", "submit-approval", "approve", "release"], actions);
        Assert.Equal("Add the hold time unit", wi.GetProperty("transitions")[2].Str("comment"));

        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"{url}/revise", new { reason = "" })).StatusCode);
        var rev = await PostEl(approver, $"{url}/revise", new { reason = "Raise test pressure" });
        Assert.Equal("B", rev.Str("revision"));
        Assert.Equal(HttpStatusCode.Conflict, (await approver.PostAsync($"{url}/revise", new { reason = "again" })).StatusCode);
        var bUrl = $"/api/sequfloh/instructions/{rev.Id()}";
        var b = await approver.GetElAsync(bUrl);
        Assert.Equal(2, b.GetProperty("instruction").GetProperty("steps").GetArrayLength());
        var (_, approver2) = await app.CreateUserAsync(ApproverPerms, site); // the reviser authored revision B, so someone else approves it
        await approver.PostOkAsync($"{bUrl}/actions/submit-review", new { });
        await approver.PostOkAsync($"{bUrl}/actions/submit-approval", new { });
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"{bUrl}/actions/approve", new { })).StatusCode);
        await approver2.PostOkAsync($"{bUrl}/actions/approve", new { });
        await approver2.PostOkAsync($"{bUrl}/actions/release", new { effectiveDate = "2026-12-01" });
        Assert.Equal("Superseded", (await author.GetElAsync(url)).GetProperty("instruction").Str("status"));
        Assert.Equal(2, (await author.GetElAsync(bUrl)).GetProperty("revisions").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync($"{bUrl}/actions/obsolete", new { })).StatusCode);
        await approver.PostOkAsync($"{bUrl}/actions/obsolete", new { comment = "Product discontinued" });
        await approver.PostOkAsync($"{bUrl}/actions/archive", new { });
        await approver.PostOkAsync($"{bUrl}/actions/restore", new { });
        Assert.Equal("Obsolete", (await author.GetElAsync(bUrl)).GetProperty("instruction").Str("status"));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == id && a.Action == "WorkInstructionRelease"));
    }

    [Fact]
    public async Task Repair_instruction_copies_library_data_without_linking_back_and_fai_characteristics_are_controlled()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([.. AuthorPerms, Permissions.SequflohManageOperationSteps, Permissions.SequflohManagePartsLists, Permissions.SequflohEditParts, Permissions.MasterDataEdit], site);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var partId = (await PostEl(author, "/api/sequfloh/parts", new
        {
            part = new { partNumber = "RW-" + tag, name = "Valve body", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = site, status = 0, hasSpecialCharacteristics = false },
            execution = new { productFamily = "Valves", isRepairable = true, isSerialized = true, isLotControlled = false }
        })).Id();
        var op = await PostEl(author, "/api/sequfloh/operation-steps", new { title = "Replace seal " + tag, category = "Assembly", detailedAction = "Remove and replace the shaft seal", minimum = 10, maximum = 12, unit = "Nm" });
        var list = (await PostEl(author, "/api/sequfloh/parts-lists", new { title = "Seal kit " + tag, listType = "RepairKit" })).Id();
        await author.PostOkAsync($"/api/sequfloh/parts-lists/{list}/items", new { partNumber = "SEAL-" + tag, description = "Shaft seal", quantity = 2, unit = "EA", required = true, traceability = "Lot" });

        var id = (await PostEl(author, "/api/sequfloh/instructions", new { type = "RepairWork", title = "Valve seal repair", siteId = site, partId })).Id();
        var url = $"/api/sequfloh/instructions/{id}";
        Assert.Equal(1, (await PostEl(author, $"{url}/copy-operation-steps", new { operationStepIds = new[] { op.Id() } })).GetProperty("copied").GetInt32());
        Assert.Equal(1, (await PostEl(author, $"{url}/copy-parts", new { partsListId = list })).GetProperty("copied").GetInt32());
        var wi = await author.GetElAsync(url);
        var step = wi.GetProperty("instruction").GetProperty("steps")[0];
        Assert.Equal("Remove and replace the shaft seal", step.Str("detailedRepairAction"));
        Assert.True(wi.GetProperty("instruction").GetProperty("partsRows")[0].GetProperty("lotNumberRequired").GetBoolean());
        Assert.Contains("Choose the repair description.", wi.GetProperty("releaseProblems").EnumerateArray().Select(x => x.GetString()));
        // edit the copy: the library template does not change
        var edited = new { id = step.Str("id"), sequence = 10, title = "Replace seal (edited)", detailedRepairAction = "Changed", minimum = 10, maximum = 12 };
        await PutOk(author, url, Save(wi, site, steps: [edited], parts: [new { replacementPartNumber = "SEAL-" + tag, quantity = 2, required = true }], repairDescription: "Component replacement"));
        Assert.Equal("Remove and replace the shaft seal", (await author.GetElAsync($"/api/sequfloh/operation-steps/{op.Id()}")).Str("detailedAction"));
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync(url, Save(wi, site, repairDescription: "Not a seeded description"))).StatusCode);
        Assert.Equal(["Enter the effective date before release."], (await author.GetElAsync(url)).GetProperty("releaseProblems").EnumerateArray().Select(p => p.GetString()).ToList());

        // FAI characteristics
        var fai = (await PostEl(author, "/api/sequfloh/instructions", new { type = "Fai", title = "Housing FAI", siteId = site })).Id();
        var furl = $"/api/sequfloh/instructions/{fai}";
        var f = await author.GetElAsync(furl);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync(furl, Save(f, site, chars: [new { characteristicNumber = "1", requirement = "Bore", lowerLimit = 10.02, upperLimit = 10.0 }]))).StatusCode);
        await PutOk(author, furl, Save(f, site, chars: [new { characteristicNumber = "1", requirement = "Bore diameter", lowerLimit = 9.98, upperLimit = 10.02, unit = "mm" }, new { characteristicNumber = "1", requirement = "Dup" }]));
        Assert.Contains("Characteristic numbers must be unique.", (await author.GetElAsync(furl)).GetProperty("releaseProblems").EnumerateArray().Select(x => x.GetString()));
        var fai2 = (await PostEl(author, "/api/sequfloh/instructions", new { type = "Fai", title = "Housing FAI 2", siteId = site })).Id();
        Assert.Equal(2, (await PostEl(author, $"/api/sequfloh/instructions/{fai2}/copy-characteristics", new { fromInstructionId = fai })).GetProperty("copied").GetInt32());
        // stale save is refused
        var cur = (await author.GetElAsync(furl)).GetProperty("instruction").GetProperty("rowVersion").GetUInt32();
        Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsync(furl, Save(f, site, rowVersion: cur + 999))).StatusCode);
    }

    [Fact]
    public async Task Fai_characteristics_import_from_an_effective_inspection_plan_and_need_inspection_read_access()
    {
        var site = await app.CreateSiteAsync();
        var (authorId, author) = await app.CreateUserAsync([.. AuthorPerms, Permissions.InspectionView], site);
        var (_, blind) = await app.CreateUserAsync(AuthorPerms, site);
        var planId = Guid.NewGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rev = new Domain.Quality.InspectionPlanRevision { PlanId = planId, PlanNumber = "IP-T-" + planId.ToString("N")[..6], Revision = "B", Status = Domain.Quality.PlanRevisionStatus.Effective, SiteId = site,
                Characteristics = [new() { Sequence = 1, Name = "Bore diameter", Target = 6.0m, LowerLimit = 5.9m, UpperLimit = 6.1m, Unit = "mm", Method = "Bore gauge", SampleSize = 5 },
                                   new() { Sequence = 2, Name = "Surface finish", Specification = "Ra 1.6 max", IsCritical = true }] };
            db.InspectionPlans.Add(new Domain.Quality.InspectionPlan { Id = planId, Number = rev.PlanNumber, Title = "Housing receiving", SiteId = site, OwnerId = authorId, EffectiveRevisionId = rev.Id });
            db.InspectionPlanRevisions.Add(rev);
            await db.SaveChangesAsync();
        }
        var wi = await PostEl(author, "/api/sequfloh/instructions", new { type = "Fai", title = "Housing FAI", siteId = site });
        var url = $"/api/sequfloh/instructions/{wi.Str("id")}";
        var sources = await author.GetElAsync($"{url}/characteristic-sources");
        Assert.Contains(sources.EnumerateArray(), x => x.Str("id") == planId.ToString() && x.GetProperty("count").GetInt32() == 2);
        Assert.Equal(0, (await blind.GetElAsync($"{url}/characteristic-sources")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await blind.PostAsync($"{url}/import-characteristics", new { inspectionPlanId = planId })).StatusCode);
        var r = await PostEl(author, $"{url}/import-characteristics", new { inspectionPlanId = planId });
        Assert.Equal(2, r.GetProperty("copied").GetInt32());
        var chars = (await author.GetElAsync(url)).GetProperty("instruction").GetProperty("characteristics").EnumerateArray().ToList();
        Assert.Equal(["1", "2"], chars.Select(c => c.Str("characteristicNumber")).ToArray());
        Assert.Equal("Bore diameter", chars[0].Str("requirement"));
        Assert.Equal(5.9m, chars[0].GetProperty("lowerLimit").GetDecimal());
        Assert.Contains("(critical)", chars[1].Str("notes"));
        await PostEl(author, $"{url}/import-characteristics", new { inspectionPlanId = planId });
        var again = (await author.GetElAsync(url)).GetProperty("instruction").GetProperty("characteristics").EnumerateArray().Select(c => c.Str("characteristicNumber")).ToList();
        Assert.Equal(4, again.Count);
        Assert.Equal(again.Count, again.Distinct().Count());
    }

    [Fact]
    public async Task Each_instruction_type_prints_a_controlled_pdf_and_printing_is_audited()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(AuthorPerms, site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.SequflohAccess], await app.CreateSiteAsync());
        foreach (var type in new[] { "Validation", "ValidationFai", "Fai", "RepairTest", "RepairWork" })
        {
            var c = await PostEl(author, "/api/sequfloh/instructions", new { type, title = $"Print {type}", siteId = site });
            var url = $"/api/sequfloh/instructions/{c.Str("id")}";
            var wi = await author.GetElAsync(url);
            await PutOk(author, url, Save(wi, site, content: new { purpose = new { purpose = "Line one\nLine two" } }, steps: [new { sequence = 10, title = "First step" }],
                chars: type is "Fai" or "ValidationFai" ? [new { sequence = 10, characteristicNumber = "1", requirement = "Length 10 mm" }] : null,
                parts: type == "RepairWork" ? [new { sequence = 10, replacementPartNumber = "P-1", quantity = 2 }] : null));
            var r = await author.GetAsync($"{url}/print");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
            var bytes = await r.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"{url}/print")).StatusCode);
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == Guid.Parse(c.Str("id")) && a.Action == "WorkInstructionPrinted"));
        }
    }
}
