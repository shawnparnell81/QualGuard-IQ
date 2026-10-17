using System.Net;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class AccountTests(TestApp app)
{
    private sealed record Settings(string ApplicationName, string OrganizationName, string? PrimaryColor, string TimeZone, bool LoginHistoryEnabled, uint RowVersion);
    private sealed record SignIn(DateTimeOffset OccurredAt, string EventType, bool Succeeded);
    private sealed record History(bool Enabled, List<SignIn> Items);

    private static async Task SetLoginHistoryAsync(ApiClient admin, bool on)
    {
        var s = await admin.GetJsonAsync<Settings>("/api/organization/settings");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/organization/settings", s with { LoginHistoryEnabled = on })).StatusCode);
    }

    /// <summary>Users see only their own sign-in activity, and only while the organization has login history turned on.</summary>
    [Fact]
    public async Task Sign_in_history_follows_the_organization_setting_and_is_per_user()
    {
        var admin = await app.AdminAsync();
        await SetLoginHistoryAsync(admin, false);
        var (_, user) = await app.CreateUserAsync([]);
        var off = await user.GetJsonAsync<History>("/api/account/sign-ins");
        Assert.False(off.Enabled);
        Assert.Empty(off.Items);

        await SetLoginHistoryAsync(admin, true);
        try
        {
            var on = await user.GetJsonAsync<History>("/api/account/sign-ins");
            Assert.True(on.Enabled);
            var own = Assert.Single(on.Items);
            Assert.Equal("SignIn", own.EventType);
            Assert.True(own.Succeeded);
            var adminHistory = await admin.GetJsonAsync<History>("/api/account/sign-ins");
            Assert.DoesNotContain(adminHistory.Items, i => i.OccurredAt == own.OccurredAt);
        }
        finally { await SetLoginHistoryAsync(admin, false); }
    }
}
