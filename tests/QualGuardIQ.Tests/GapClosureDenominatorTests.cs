using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 7: scrap and rework rates use a configurable denominator hierarchy that prefers actual production volume and labels fallbacks.</summary>
[Collection("api")]
public sealed class GapClosureDenominatorTests(TestApp app)
{
    private static DateOnly Today => TestDates.Today;

    async Task<System.Text.Json.JsonElement> Scrap(ApiClient c, Guid site) =>
        (await c.GetElAsync($"/api/insights/production?siteId={site}&from={Today.AddDays(-5):yyyy-MM-dd}&to={Today:yyyy-MM-dd}")).EnumerateArray().First(x => x.Str("key") == "scrap_rate");

    async Task Add(params object[] rows)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var r in rows) db.Add(r);
        await db.SaveChangesAsync();
    }

    ProductionEntry Entry(Guid site, RecordSource src, decimal good) => new() { Kind = ProductionEntryKind.Production, Good = good, Rejected = 0, Scrapped = 0, FirstPass = true, Source = src, RecordedAt = DateTimeOffset.UtcNow.AddHours(-2), SiteId = site };

    [Fact]
    public async Task Rates_prefer_production_volume_label_fallbacks_and_never_divide_by_zero()
    {
        var site = await app.CreateSiteAsync();
        var perms = new[] { Permissions.AnalyticsView, Permissions.CoqView, Permissions.CoqEdit, Permissions.ProductionView, Permissions.ProductionEdit, Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.InspectionView };
        var (_, a) = await app.CreateUserAsync(perms, site);
        var (_, b) = await app.CreateUserAsync(perms, site);
        var admin = await app.AdminAsync();
        var part = (await (await a.PostOkAsync("/api/master/parts", new { partNumber = "DN-" + Guid.NewGuid().ToString("N")[..6], name = "Bracket", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();
        await a.PostOkAsync("/api/coq/scrap", new { scrappedOn = Today, partId = part, quantity = 5m, unitCost = 1m, reason = "Burr", siteId = site });

        // No production data: no value, numerator kept, explicit reason; inspected quantity is not used silently.
        var none = await Scrap(a, site);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, none.GetProperty("value").ValueKind);
        Assert.Equal(5m, none.GetProperty("numerator").GetDecimal());
        Assert.Contains("cannot be calculated", none.Str("denominatorLabel"));
        Assert.DoesNotContain("inspected", none.Str("denominatorLabel"));
        Assert.Equal("rate/2 (configurable denominator)", none.Str("calculationVersion"));

        // Manual volume counts only after a second person approves it; it is the last default source, so it is a labelled fallback.
        var vol = (await (await a.PostOkAsync("/api/insights/production-volumes", new { siteId = site, periodStart = Today.AddDays(-5), periodEnd = Today, quantity = 600m, notes = "Shift reports" })).JsonAsync()).Id();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await Scrap(a, site)).GetProperty("value").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsync($"/api/insights/production-volumes/{vol}/approve")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/api/insights/production-volumes/{vol}/approve")).StatusCode);
        var manual = await Scrap(a, site);
        Assert.Equal("ManuallyApproved", manual.Str("denominatorSource"));
        Assert.True(manual.GetProperty("isFallback").GetBoolean());
        Assert.StartsWith("Fallback denominator: approved manual production volume", manual.Str("denominatorLabel"));
        Assert.Equal(600m, manual.GetProperty("denominator").GetDecimal());
        Assert.Equal(0.8m, manual.GetProperty("value").GetDecimal());

        // Work orders completed, then locally recorded production, then ERP (mirror) production take precedence in turn.
        await Add(new WorkOrder { Number = "WO-" + Guid.NewGuid().ToString("N")[..6], ItemNumber = "X", Quantity = 400, QuantityCompleted = 400, Status = WorkOrderStatus.Completed, SiteId = site });
        Assert.Equal("Completed", (await Scrap(a, site)).Str("denominatorSource"));
        await Add(Entry(site, RecordSource.Local, 250));
        var produced = await Scrap(a, site);
        Assert.Equal("Produced", produced.Str("denominatorSource")); Assert.Equal(250m, produced.GetProperty("denominator").GetDecimal()); Assert.True(produced.GetProperty("isFallback").GetBoolean());
        await Add(Entry(site, RecordSource.Erp, 1000));
        var erp = await Scrap(a, site);
        Assert.Equal("ErpProduction", erp.Str("denominatorSource")); Assert.Equal(1000m, erp.GetProperty("denominator").GetDecimal()); Assert.Equal(0.5m, erp.GetProperty("value").GetDecimal());
        Assert.Contains("ERP production quantity", erp.Str("definition"));
        // MES actual production is the first preference: no fallback label.
        await Add(Entry(site, RecordSource.Mes, 200));
        var mes = await Scrap(a, site);
        Assert.Equal("MesProduction", mes.Str("denominatorSource")); Assert.False(mes.GetProperty("isFallback").GetBoolean());
        Assert.Equal("Denominator: MES production quantity", mes.Str("denominatorLabel")); Assert.Equal(2.5m, mes.GetProperty("value").GetDecimal());

        // Site configuration: shipped only, no fallback → no value rather than a substitute.
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PutAsync("/api/insights/denominators", new { metric = "scrap_rate", siteId = site, sources = new[] { "Shipped" }, allowFallback = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/insights/denominators", new { metric = "scrap_rate", siteId = site, sources = new[] { "Guesswork" }, allowFallback = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/insights/denominators", new { metric = "fpy", siteId = site, sources = new[] { "Shipped" }, allowFallback = false })).StatusCode);
        var put = await admin.PutAsync("/api/insights/denominators", new { metric = "scrap_rate", siteId = site, sources = new[] { "Shipped", "MesProduction" }, allowFallback = false });
        Assert.True(put.StatusCode == HttpStatusCode.NoContent, await put.Content.ReadAsStringAsync());
        var strict = await Scrap(a, site);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, strict.GetProperty("value").ValueKind);
        Assert.Contains("quantity shipped", strict.Str("denominatorLabel"));
        Assert.DoesNotContain("MES", strict.Str("denominatorLabel"));
        // Inspected is used only when configured, and then it says so.
        await admin.PutAsync("/api/insights/denominators", new { metric = "scrap_rate", siteId = site, sources = new[] { "Inspected", "Received", "MesProduction" }, allowFallback = true });
        var cfg = await a.GetElAsync($"/api/insights/denominators?siteId={site}");
        Assert.Equal("Inspected", cfg.GetProperty("metrics")[0].GetProperty("sources")[0].GetString());
        var fb = await Scrap(a, site);
        Assert.Equal("MesProduction", fb.Str("denominatorSource")); // no inspections or receipts at this site
        Assert.Equal("Fallback denominator: MES production quantity (no quantity inspected in the period)", fb.Str("denominatorLabel"));

        // Reproducible history: warehouse facts store the source and calculation version.
        await admin.PostOkAsync($"/api/insights/snapshots?period=0&date={Today:yyyy-MM-dd}");
        using var scope = app.Services.CreateScope();
        var fact = await scope.ServiceProvider.GetRequiredService<AppDbContext>().QualityMetricFacts.AsNoTracking().FirstAsync(x => x.Metric == "scrap_rate" && x.SiteId == site);
        Assert.Equal("Fallback:MesProduction", fact.DenominatorSource);
        Assert.Equal("rate/2 (configurable denominator)", fact.CalculationVersion);
    }
}
