using System.Net;
using System.Net.Http.Json;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class TimeZoneTests(TestApp app)
{
    private async Task<Guid> SiteAsync(string tz)
    {
        var admin = await app.AdminAsync();
        return await (await admin.PostOkAsync("/api/organization/sites", new { code = "Z" + Guid.NewGuid().ToString("N")[..8], name = tz, address = (string?)null, timeZone = tz })).Content.ReadFromJsonAsync<Guid>();
    }

    private static DateOnly TodayIn(string tz) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tz)).DateTime);

    /// <summary>"Today" follows each site's time zone: UTC+14 and UTC-11 are always on different dates.</summary>
    [Fact]
    public async Task Business_dates_follow_the_site_time_zone()
    {
        var east = await SiteAsync("Pacific/Kiritimati");
        var west = await SiteAsync("Pacific/Pago_Pago");
        var (_, east_user) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], east);
        var (_, west_user) = await app.CreateUserAsync([Permissions.NcrView, Permissions.NcrEdit], west);
        var eastToday = TodayIn("Pacific/Kiritimati");
        Assert.True(eastToday > TodayIn("Pacific/Pago_Pago"));
        object Ncr(Guid site) => new { title = "Date check", description = "Date check", source = "Other", siteId = site, departmentId = (Guid?)null, ownerId = (Guid?)null, partNumber = (string?)null, lotNumber = (string?)null, quantityAffected = (decimal?)null, detectedOn = eastToday, severity = 0 };
        Assert.Equal(HttpStatusCode.OK, (await east_user.PostAsync("/api/ncrs", Ncr(east))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await west_user.PostAsync("/api/ncrs", Ncr(west))).StatusCode); // still yesterday there
    }

    [Fact]
    public async Task Unknown_site_time_zone_is_rejected_and_default_is_new_york()
    {
        var admin = await app.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/organization/sites", new { code = "BADTZ", name = "x", address = (string?)null, timeZone = "Mars/Olympus" })).StatusCode);
        var id = await (await admin.PostOkAsync("/api/organization/sites", new { code = "D" + Guid.NewGuid().ToString("N")[..8], name = "Default", address = (string?)null })).Content.ReadFromJsonAsync<Guid>();
        var sites = await admin.GetJsonAsync<List<SiteTz>>("/api/organization/sites");
        Assert.Equal("America/New_York", sites.Single(s => s.Id == id).TimeZone);
    }

    private sealed record SiteTz(Guid Id, string TimeZone);
}
