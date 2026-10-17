using System.Net;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Apqp;

namespace QualGuardIQ.Tests;

/// <summary>
/// Gap 6: bias t-test with exact Student t quantiles and p-values (MathNet.Numerics). Reference values were computed independently
/// with SciPy 1.x (scipy.stats.t.ppf and scipy.stats.ttest_1samp), not with the code under test.
/// </summary>
[Collection("api")]
public sealed class GapClosureBiasTests(TestApp app)
{
    static readonly decimal[] Fifteen = [6.0m, 6.1m, 5.9m, 6.2m, 6.0m, 6.3m, 6.1m, 5.8m, 6.2m, 6.1m, 6.0m, 6.4m, 6.2m, 6.1m, 6.0m];
    static readonly decimal[] Ten = [10.02m, 9.98m, 10.01m, 10.03m, 10.00m, 9.99m, 10.02m, 10.01m, 10.00m, 10.02m];

    [Fact]
    public void Fifteen_readings_match_scipy_ttest_1samp()
    {
        var b = MsaMath.Bias(Fifteen, 6.0m, 1m);
        Assert.Equal(14, b.DegreesOfFreedom);
        Assert.Equal(0.0933m, b.Bias);
        Assert.Equal(2.3568m, b.T);            // scipy 2.356831744
        Assert.Equal(0.033524m, b.PValue);     // scipy 0.033523900
        Assert.Equal(2.1448m, b.CriticalT);    // t.ppf(0.975, 14) = 2.144786688
        Assert.True(b.Significant);
        Assert.Equal(MsaMath.BiasCalculationVersion, b.CalculationVersion);
        // The old five-step table used 2.093 (19 dof) here; at 99% the exact quantile (2.977) makes the same data not significant.
        var b99 = MsaMath.Bias(Fifteen, 6.0m, 1m, 0.99m);
        Assert.Equal(2.9768m, b99.CriticalT);  // t.ppf(0.995, 14) = 2.976842734
        Assert.False(b99.Significant);
    }

    [Fact]
    public void Ten_readings_match_scipy_and_bias_interval_contains_zero_when_not_significant()
    {
        var b = MsaMath.Bias(Ten, 10.0m, null);
        Assert.Equal(9, b.DegreesOfFreedom);
        Assert.Equal(1.633m, Math.Round(b.T!.Value, 3));   // scipy 1.632993162
        Assert.Equal(0.136904m, b.PValue);                  // scipy 0.136904126
        Assert.Equal(2.2622m, b.CriticalT);                 // t.ppf(0.975, 9) = 2.262157163
        Assert.False(b.Significant);
        Assert.True(b.BiasLow < 0 && b.BiasHigh > 0);
        Assert.Null(b.PctTolerance);
    }

    [Theory]
    [InlineData(0.975, 1, 12.7062)]   // t.ppf(0.975, 1) = 12.706204736
    [InlineData(0.975, 4, 2.7764)]    // 2.776445105
    [InlineData(0.995, 19, 2.8609)]   // 2.860934606
    [InlineData(0.95, 29, 1.6991)]    // one-sided 95% = two-sided 90%: 1.699127027
    public void Critical_values_use_the_studys_degrees_of_freedom(double p, int dof, double expected)
    {
        var confidence = (decimal)(1 - 2 * (1 - p));
        var values = Enumerable.Range(0, dof + 1).Select(i => 5m + (i % 2 == 0 ? 0.01m : -0.01m) * (i % 3 + 1)).ToList();
        Assert.Equal((decimal)expected, MsaMath.Bias(values, 5m, null, confidence).CriticalT);
    }

    [Fact]
    public void Edge_cases_are_explicit()
    {
        Assert.Throws<ArgumentException>(() => MsaMath.Bias([1.0m], 1m, null));                 // insufficient data
        Assert.Throws<ArgumentException>(() => MsaMath.Bias([1.0m, 1.1m], 1m, null, 0.5m));     // confidence out of range
        var none = MsaMath.Bias([2.0m, 2.0m, 2.0m], 2.0m, null);                                 // zero variance, no bias
        Assert.Null(none.T); Assert.False(none.Significant); Assert.Equal(1m, none.PValue);
        var some = MsaMath.Bias([2.1m, 2.1m, 2.1m], 2.0m, 1m);                                   // zero variance, real bias
        Assert.Null(some.T); Assert.True(some.Significant); Assert.Contains("undefined", some.Note);
        Assert.Contains("Fewer than 10", MsaMath.Bias([1.0m, 1.1m, 0.9m], 1m, null).Note);
    }

    [Fact]
    public async Task Submitted_results_keep_their_version_and_recalculation_adds_a_new_one()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.SpcView, Permissions.SpcEdit, Permissions.CalibrationView, Permissions.CalibrationEdit], site);
        var eq = (await (await eng.PostOkAsync("/api/calibration/equipment", new { name = "Height gauge " + Guid.NewGuid().ToString("N")[..5], siteId = site, calibrationIntervalDays = 365, nextDueOn = TestDates.Today.AddDays(100) })).JsonAsync()).Id();
        object Study(decimal? conf) => new { title = "Height bias", studyType = 2, equipmentId = eq, tolerance = 1m, referenceValue = 6.0m, reviewIntervalMonths = 12, siteId = site, confidenceLevel = conf };
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync("/api/spc/msa", Study(0.5m))).StatusCode);
        var id = (await (await eng.PostOkAsync("/api/spc/msa", Study(0.99m))).JsonAsync()).Id();
        await eng.PutAsync($"/api/spc/msa/{id}/measurements", Fifteen.Select((v, i) => new { part = 1, trial = i + 1, value = (decimal?)v }).ToArray());
        var preview = await eng.GetElAsync($"/api/spc/msa/{id}");
        var r = preview.GetProperty("result");
        Assert.Equal(2.9768m, r.GetProperty("criticalT").GetDecimal());
        Assert.Equal(14, r.GetProperty("degreesOfFreedom").GetInt32());
        Assert.False(r.GetProperty("significant").GetBoolean());
        Assert.Equal("Acceptable", preview.Str("verdict"));
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/spc/msa/{id}/recalculate")).StatusCode); // draft: nothing frozen yet

        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/spc/msa/{id}/submit")).StatusCode);
        var study = (await eng.GetElAsync($"/api/spc/msa/{id}")).GetProperty("study");
        Assert.Equal(MsaMath.BiasCalculationVersion, study.Str("calculationVersion"));
        var frozen = study.Str("result");
        var again = await (await eng.PostOkAsync($"/api/spc/msa/{id}/recalculate")).JsonAsync();
        Assert.Equal(MsaMath.BiasCalculationVersion, again.Str("calculationVersion"));
        var versions = (await eng.GetElAsync($"/api/spc/msa/{id}/results")).EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);
        Assert.True(versions[0].GetProperty("isOfRecord").GetBoolean());
        Assert.Equal("Recalculated", versions[1].Str("reason"));
        Assert.Equal(frozen, (await eng.GetElAsync($"/api/spc/msa/{id}")).GetProperty("study").Str("result")); // result of record unchanged
    }
}
