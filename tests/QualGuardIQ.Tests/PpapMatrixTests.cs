using System.Net;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Enterprise;

namespace QualGuardIQ.Tests;

/// <summary>
/// Checks every cell of the PPAP submission level matrix against AIAG PPAP 4th Edition Table 4.2
/// "Retention/Submission Requirements", transcribed here independently of PpapMatrix.
/// Columns: levels 1-5. S = submit and retain, R = retain (available on request), * = retain, submit on request.
/// Cross-checked cell by cell on 2026-10-17 against two published reproductions:
/// https://quality-one.com/wp-content/uploads/2016/12/PPAP-Submission-Levels.pdf and
/// https://www.accurate-prod.com/wp-content/uploads/2024/04/ppap-level-requirements.pdf; both agree.
/// </summary>
[Collection("api")]
public sealed class PpapMatrixTests(TestApp app)
{
    private const string Pw = "long-enough-password";

    // Tracked elements 1-18 (element 1 = design records for all other, non-proprietary, components/details).
    private static readonly Dictionary<int, string> Published = new()
    {
        [1] = "RSS*R", [2] = "RSS*R", [3] = "RRS*R", [4] = "RRS*R", [5] = "RRS*R", [6] = "RRS*R",
        [7] = "RRS*R", [8] = "RRS*R", [9] = "RSS*R", [10] = "RSS*R", [11] = "RRS*R", [12] = "RSS*R",
        [13] = "SSS*R", [14] = "RSS*R", [15] = "RRR*R", [16] = "RRR*R", [17] = "RRS*R", [18] = "SSSSR",
    };

    private static PpapRequirement Expected(char c) => c switch { 'S' => PpapRequirement.Submit, 'R' => PpapRequirement.Retain, '*' => PpapRequirement.SubmitOnRequest, _ => throw new ArgumentOutOfRangeException(nameof(c)) };

    [Fact]
    public void All_90_element_cells_match_the_published_table()
    {
        var mismatches = new List<string>();
        for (var code = 1; code <= 18; code++)
        for (var level = 1; level <= 5; level++)
        {
            var expected = Expected(Published[code][level - 1]);
            var actual = ProjectService.RequirementFor(level, code);
            if (actual != expected) mismatches.Add($"Element {code} level {level}: expected {expected}, got {actual}");
        }
        Assert.Empty(mismatches);
        Assert.Equal(18, ProjectService.PpapElements.Length);
    }

    [Fact]
    public void Reference_rows_and_conditions_match_the_published_table()
    {
        string Row(string key) => PpapMatrix.Rows.Single(r => r.Key == key).Codes;
        Assert.Equal("RRR*R", Row("1a"));  // design records, proprietary components/details
        Assert.Equal("RSS*R", Row("1"));   // design records, all other components/details
        Assert.Equal("SSSSR", Row("BMC")); // bulk material requirements checklist
        Assert.Equal(20, PpapMatrix.Rows.Length);
        Assert.All(PpapMatrix.Rows, r => Assert.Matches("^[SR*]{5}$", r.Codes));
        Assert.Equal("If any", PpapMatrix.Rows.Single(r => r.ElementCode == 2).Condition);
        Assert.Equal("If required", PpapMatrix.Rows.Single(r => r.ElementCode == 3).Condition);
        Assert.Equal("If applicable", PpapMatrix.Rows.Single(r => r.ElementCode == 13).Condition);
        Assert.Equal(new[] { "S", "R", "*" }, PpapMatrix.Legend.Select(l => l.Code));
        Assert.Throws<ArgumentOutOfRangeException>(() => PpapMatrix.RequirementFor(6, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PpapMatrix.RequirementFor(3, 19));
    }

    /// <summary>A submission's elements, the matrix, legend and codes come back through the API; conditional elements can be N/A; the PSW is always needed, including at level 5 where it is R.</summary>
    [Fact]
    public async Task Submissions_carry_matrix_codes_and_legend_and_always_need_the_psw()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.ApqpView, Permissions.ApqpEdit, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var partId = (await (await eng.PostOkAsync("/api/master/parts", new { partNumber = "PM-" + tag, name = "Bracket", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();

        for (var level = 1; level <= 5; level++)
        {
            var id = (await (await eng.PostOkAsync("/api/launch/ppap", new { partId, partRevision = "A", level, reason = "Initial submission", siteId = site })).JsonAsync()).Id();
            var body = await eng.GetElAsync($"/api/launch/ppap/{id}");
            foreach (var e in body.GetProperty("elements").EnumerateArray())
                Assert.Equal(Published[e.Int("code")][level - 1].ToString(), e.Str("requirementCode"));
            var matrix = body.GetProperty("matrix").EnumerateArray().ToList();
            Assert.Equal(20, matrix.Count);
            Assert.Equal("R", matrix.Single(r => r.Str("key") == "18").GetProperty("levels")[4].GetString());
            Assert.Equal(3, body.GetProperty("legend").GetArrayLength());

            if (level == 5)
            {
                Assert.Equal("Retain", body.GetProperty("elements").EnumerateArray().Single(e => e.Int("code") == 18).Str("requirement"));
                Assert.Equal(HttpStatusCode.BadRequest, (await eng.PutAsync($"/api/launch/ppap/{id}/elements/18", new { status = 2, linkedType = (string?)null, linkedId = (Guid?)null, notes = "n/a" })).StatusCode);
                var missing = await eng.PostAsync($"/api/launch/ppap/{id}/submit");
                Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
                Assert.Contains("18 Part submission warrant", await missing.Content.ReadAsStringAsync());
                Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/launch/ppap/{id}/elements/18", new { status = 1, linkedType = (string?)null, linkedId = (Guid?)null, notes = "PSW signed" })).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/launch/ppap/{id}/submit")).StatusCode);
            }
            if (level == 3)
            {
                // "if required" / "if applicable" elements may be marked not applicable; S elements 3 and 17 block submission otherwise
                Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/launch/ppap/{id}/elements/3", new { status = 2, linkedType = (string?)null, linkedId = (Guid?)null, notes = "Not required by customer" })).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/launch/ppap/{id}/elements/13", new { status = 2, linkedType = (string?)null, linkedId = (Guid?)null, notes = "Not an appearance item" })).StatusCode);
                var r = await (await eng.PostAsync($"/api/launch/ppap/{id}/submit")).Content.ReadAsStringAsync();
                Assert.Contains("17 Customer-specific requirements", r);
                Assert.DoesNotContain("3 Customer engineering approval", r);
                Assert.DoesNotContain("15 Master sample", r);
            }
        }
    }
}
