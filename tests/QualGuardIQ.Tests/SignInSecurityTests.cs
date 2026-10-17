using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Security;

namespace QualGuardIQ.Tests;

/// <summary>
/// Independent RFC 6238 generator for the tests (HMAC-SHA1, 30 s, 6 digits), so the server's implementation is checked against
/// a second one rather than against itself.
/// </summary>
public static class TestTotp
{
    public static byte[] FromBase32(string key)
    {
        const string a = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(key.Replace(" ", "").ToUpperInvariant().Select(c => Convert.ToString(a.IndexOf(c), 2).PadLeft(5, '0')));
        return Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
    }

    public static string At(byte[] secret, long counter, int digits = 6)
    {
        var msg = BitConverter.GetBytes(counter); if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        using var h = new HMACSHA1(secret);
        var hash = h.ComputeHash(msg);
        var o = hash[^1] & 0xf;
        var v = ((hash[o] & 0x7f) << 24) | (hash[o + 1] << 16) | (hash[o + 2] << 8) | hash[o + 3];
        return (v % (int)Math.Pow(10, digits)).ToString(new string('0', digits));
    }

    /// <summary>Code for the current 30-second step plus an offset (-1, 0, +1 are accepted). Waits out the last second of a step so the code cannot roll over in flight.</summary>
    public static string Now(string key, int offset = 0)
    {
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 30 >= 29) Thread.Sleep(1500);
        return At(FromBase32(key), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30 + offset);
    }
}

[Collection("api")]
public sealed class SignInSecurityTests(TestApp app)
{
    const string Pw = "long-enough-password";

    sealed record Step(string Next, string Challenge, int ExpiresInSeconds, bool TrustAvailable, int TrustDays, string? Reason, int RecoveryCodesLeft);

    static async Task<Step> StepAsync(HttpResponseMessage r)
    {
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<Step>())!;
    }

    async Task<string> EmailOf(Guid id) => (await (await app.AdminAsync()).GetJsonAsync<List<UserDto>>("/api/admin/users")).Single(u => u.Id == id).Email;

    /// <summary>Signed-in user who turned on two-step verification from the Security page (uses the code for step -1).</summary>
    async Task<(Guid Id, string Email, string Key, List<string> Codes, ApiClient Client)> EnrolledUserAsync()
    {
        var (id, c) = await app.CreateUserAsync([Permissions.NcrView]);
        var setup = await (await c.PostOkAsync("/api/account/mfa/setup", new { password = Pw })).JsonAsync();
        var key = setup.Str("key");
        Assert.StartsWith("otpauth://totp/", setup.Str("otpAuthUri"));
        Assert.Contains("<svg", setup.Str("qrSvg"));
        var en = await (await c.PostOkAsync("/api/account/mfa/enable", new { code = TestTotp.Now(key, -1) })).JsonAsync();
        var codes = en.GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToList();
        await c.RefreshCsrfAsync();
        return (id, await EmailOf(id), key, codes, c);
    }

    [Fact]
    public void Totp_matches_the_RFC_6238_test_vectors_and_base32_round_trips()
    {
        var secret = "12345678901234567890"u8.ToArray();
        foreach (var (t, expected) in new[] { (59L, "94287082"), (1111111109L, "07081804"), (1111111111L, "14050471"), (1234567890L, "89005924"), (2000000000L, "69279037"), (20000000000L, "65353130") })
        {
            Assert.Equal(expected, Totp.Code(secret, t / 30, 8));
            Assert.Equal(expected, TestTotp.At(secret, t / 30, 8));
        }
        var key = Base32.Encode(secret);
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", key);
        Assert.Equal(secret, Base32.Decode(key));
        // replay protection: a step at or before the last accepted one is refused
        var now = DateTimeOffset.FromUnixTimeSeconds(1_000_000_020);
        var step = Totp.TimeStep(now);
        Assert.Equal(step, Totp.Match(secret, Totp.Code(secret, step), now, 1, null));
        Assert.Null(Totp.Match(secret, Totp.Code(secret, step), now, 1, step));
        Assert.Null(Totp.Match(secret, Totp.Code(secret, step - 2), now, 1, null));
    }

    [Fact]
    public async Task Enrolled_user_needs_a_code_codes_cannot_be_replayed_and_recovery_codes_work_once()
    {
        var u = await EnrolledUserAsync();
        Assert.Equal(10, u.Codes.Count);
        Assert.All(u.Codes, x => Assert.Matches("^[a-z2-9]{5}-[a-z2-9]{5}$", x));
        var sec = await u.Client.GetElAsync("/api/account/security");
        Assert.True(sec.GetProperty("mfaEnabled").GetBoolean());
        Assert.Equal(10, sec.GetProperty("recoveryCodesLeft").GetInt32());

        var c = await app.AnonymousAsync();
        var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }));
        Assert.Equal("mfa", step.Next);
        Assert.InRange(step.ExpiresInSeconds, 280, 300);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode); // no session before the code

        var wrong = await c.PostAsync("/api/auth/mfa/verify", new { challenge = step.Challenge, code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("code_invalid", (await wrong.JsonAsync()).Str("code"));
        var code = TestTotp.Now(u.Key, 0);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/mfa/verify", new { challenge = step.Challenge, code })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);

        // the same code again (another browser) is refused
        var c2 = await app.AnonymousAsync();
        var s2 = await StepAsync(await c2.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c2.PostAsync("/api/auth/mfa/verify", new { challenge = s2.Challenge, code })).StatusCode);
        // a recovery code works once (any case, with or without the dash)
        var rc = u.Codes[3];
        Assert.Equal(HttpStatusCode.NoContent, (await c2.PostAsync("/api/auth/mfa/verify", new { challenge = s2.Challenge, code = rc.Replace("-", "").ToUpperInvariant() })).StatusCode);
        var c3 = await app.AnonymousAsync();
        var s3 = await StepAsync(await c3.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }));
        Assert.Equal(9, s3.RecoveryCodesLeft);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c3.PostAsync("/api/auth/mfa/verify", new { challenge = s3.Challenge, code = rc })).StatusCode);

        var admin = await app.AdminAsync();
        var events = await admin.GetJsonAsync<PageDto<SecurityEventDto>>("/api/security-events?pageSize=200");
        Assert.Contains(events.Items, e => e.EventType == "MfaFailed" && e.UserName == u.Email);
        Assert.Contains(events.Items, e => e.EventType == "RecoveryCodeUsed" && e.UserName == u.Email);
        Assert.Contains(events.Items, e => e.EventType == "SignIn" && e.UserName == u.Email && e.Detail == "Authenticator code");
        var audit = await admin.GetJsonAsync<PageDto<AuditDto>>($"/api/audit-trail?pageSize=200&entityType=User");
        Assert.Contains(audit.Items, a => a.EntityId == u.Id && a.Action == "MfaEnrolled" && a.Description.Contains("turned on two-step verification"));
        Assert.Contains(audit.Items, a => a.EntityId == u.Id && a.Action == "RecoveryCodeUsed");
    }

    [Fact]
    public async Task Step_tokens_time_out_with_a_start_again_message_and_cannot_be_forged()
    {
        var u = await EnrolledUserAsync();
        // a step token issued six minutes ago (same protector the API uses)
        var protector = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("QualGuardIQ.Auth.Challenge.v1");
        var stamp = (await app.Services.CreateScope().ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<QualGuardIQ.Infrastructure.Identity.ApplicationUser>>().FindByIdAsync(u.Id.ToString()))!.SecurityStamp;
        var old = protector.Protect(JsonSerializer.Serialize(new ChallengeTicket(u.Id, stamp!, SignInStages.Mfa, DateTimeOffset.UtcNow.AddMinutes(-1))));
        var c = await app.AnonymousAsync();
        var r = await c.PostAsync("/api/auth/mfa/verify", new { challenge = old, code = TestTotp.Now(u.Key, 1) });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        var body = await r.JsonAsync();
        Assert.Equal("challenge_expired", body.Str("code"));
        Assert.Contains("5 minutes", body.Str("title"));
        Assert.Contains("Start again", body.Str("title"));
        var forged = await c.PostAsync("/api/auth/mfa/verify", new { challenge = "not-a-token", code = "123456" });
        Assert.Equal("challenge_invalid", (await forged.JsonAsync()).Str("code"));
        // a step token for another stage is refused
        var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }));
        Assert.Equal("challenge_invalid", (await (await c.PostAsync("/api/auth/mfa/enrol/start", new { challenge = step.Challenge })).JsonAsync()).Str("code"));
    }

    [Fact]
    public async Task Role_requirement_forces_set_up_at_sign_in_and_blocks_turning_it_off()
    {
        var admin = await app.AdminAsync();
        var roleId = await (await admin.PostOkAsync("/api/admin/roles", new { name = "MFA role " + Guid.NewGuid().ToString("N")[..6], description = (string?)null, permissions = new[] { Permissions.NcrView } })).Content.ReadFromJsonAsync<Guid>();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync($"/api/admin/roles/{roleId}/mfa", new { requiresMfa = true })).StatusCode);
        var (id, _) = await app.CreateUserAsync([Permissions.NcrView]);
        await admin.PostOkAsync($"/api/admin/users/{id}/roles", new { roleId, siteId = (Guid?)null, departmentId = (Guid?)null });
        var email = await EmailOf(id);

        var c = await app.AnonymousAsync();
        var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email, password = Pw }));
        Assert.Equal("enrol", step.Next);
        Assert.Contains("requires two-step verification", step.Reason);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
        var setup = await (await c.PostOkAsync("/api/auth/mfa/enrol/start", new { challenge = step.Challenge })).JsonAsync();
        var key = setup.Str("key");
        Assert.Equal(32, key.Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/auth/mfa/enrol/confirm", new { challenge = step.Challenge, code = "111111" })).StatusCode);
        var done = await (await c.PostOkAsync("/api/auth/mfa/enrol/confirm", new { challenge = step.Challenge, code = TestTotp.Now(key, -1) })).JsonAsync();
        Assert.Equal(10, done.GetProperty("recoveryCodes").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
        await c.RefreshCsrfAsync();
        var sec = await c.GetElAsync("/api/account/security");
        Assert.True(sec.GetProperty("mfaRequired").GetBoolean());
        var off = await c.PostAsync("/api/account/mfa/disable", new { password = Pw, code = TestTotp.Now(key, 0) });
        Assert.Equal(HttpStatusCode.Conflict, off.StatusCode);
        // user tokens for scripts need the code too
        var http = app.CreateClient();
        Assert.Equal("mfa_required", (await (await http.PostAsJsonAsync("/api/auth/token", new { email, password = Pw })).JsonAsync()).Str("code"));
        var tok = await http.PostAsJsonAsync("/api/auth/token", new { email, password = Pw, code = TestTotp.Now(key, 1) });
        Assert.Equal(HttpStatusCode.OK, tok.StatusCode);
        // the requirement is listed on the policy page
        var view = await admin.GetElAsync("/api/admin/sign-in-policy");
        Assert.True(view.GetProperty("roles").EnumerateArray().Single(r => r.Id() == roleId).GetProperty("requiresMfa").GetBoolean());
        // the role change is in the audit trail
        var audit = await admin.GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?pageSize=200&entityType=Role");
        Assert.Contains(audit.Items, a => a.EntityId == roleId && (a.Changes ?? "").Contains("RequiresMfa"));
    }

    [Fact]
    public async Task Organization_requirement_applies_to_people_but_api_keys_and_oauth_clients_work_without_codes()
    {
        var admin = await app.AdminAsync();
        var site = await app.CreateSiteAsync();
        var (svcId, _) = await app.CreateUserAsync([Permissions.AuditsView], site);
        var created = await (await admin.PostOkAsync("/api/admin/api-clients", new { name = "Integration " + Guid.NewGuid().ToString("N")[..6], serviceUserId = svcId, rateLimitPerMinute = 100, expiresOn = (DateOnly?)null })).JsonAsync();
        var (personId, _) = await app.CreateUserAsync([Permissions.NcrView]);
        var person = await EmailOf(personId);
        var view = await admin.GetElAsync("/api/admin/sign-in-policy");
        var p = view.GetProperty("policy");
        try
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/admin/sign-in-policy", new { mfaRequiredForStaff = true, mfaRequiredForPortal = false, trustedDevicesEnabled = true, trustedDeviceDays = 30, rowVersion = p.GetProperty("rowVersion").GetUInt32() })).StatusCode);
            var c = await app.AnonymousAsync();
            var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email = person, password = Pw }));
            Assert.Equal("enrol", step.Next);
            Assert.Contains("all staff", step.Reason);
            var http = app.CreateClient();
            Assert.Equal("mfa_enrolment_required", (await (await http.PostAsJsonAsync("/api/auth/token", new { email = person, password = Pw })).JsonAsync()).Str("code"));

            // API key and OAuth client credentials act as the service user without any code
            var key = app.CreateClient(); key.DefaultRequestHeaders.Add("X-Api-Key", created.Str("apiKey"));
            Assert.Equal(HttpStatusCode.OK, (await key.GetAsync("/api/audits/programs")).StatusCode);
            var oauth = app.CreateClient();
            var tok = await oauth.PostAsync("/api/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials", ["client_id"] = created.Str("clientId"), ["client_secret"] = created.Str("secret") }));
            Assert.Equal(HttpStatusCode.OK, tok.StatusCode);
            oauth.DefaultRequestHeaders.Authorization = new("Bearer", (await tok.JsonAsync()).Str("accessToken"));
            Assert.Equal(HttpStatusCode.OK, (await oauth.GetAsync("/api/audits/programs")).StatusCode);
            Assert.Equal(1, (await admin.GetElAsync("/api/admin/sign-in-policy")).GetProperty("coverage").GetProperty("activeStaff").GetInt32() > 0 ? 1 : 0);
        }
        finally
        {
            var now = (await admin.GetElAsync("/api/admin/sign-in-policy")).GetProperty("policy");
            await admin.PutAsync("/api/admin/sign-in-policy", new { mfaRequiredForStaff = false, mfaRequiredForPortal = false, trustedDevicesEnabled = true, trustedDeviceDays = 30, rowVersion = now.GetProperty("rowVersion").GetUInt32() });
        }
        var audit = await admin.GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?pageSize=100&entityType=SignInPolicy");
        Assert.Contains(audit.Items, a => (a.Changes ?? "").Contains("MfaRequiredForStaff"));
    }

    [Fact]
    public async Task Trusted_browser_skips_the_code_until_revoked()
    {
        var u = await EnrolledUserAsync();
        var c = await app.AnonymousAsync();
        var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }));
        Assert.True(step.TrustAvailable);
        Assert.Equal(30, step.TrustDays);
        var r = await c.PostAsync("/api/auth/mfa/verify", new { challenge = step.Challenge, code = TestTotp.Now(u.Key, 0), trustDevice = true });
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var cookie = r.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("qms.trusted="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("path=/api;", cookie.ToLowerInvariant() + ";");
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        await c.RefreshCsrfAsync();
        var sec = await c.GetElAsync("/api/account/security");
        var device = sec.GetProperty("trustedDevices").EnumerateArray().Single();
        Assert.True(device.GetProperty("isThisBrowser").GetBoolean());
        await c.PostOkAsync("/api/auth/logout");
        await c.RefreshCsrfAsync();

        // next sign-in on this browser: no code
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw })).StatusCode);
        await c.RefreshCsrfAsync();
        // another browser still needs the code
        var other = await app.AnonymousAsync();
        Assert.Equal("mfa", (await StepAsync(await other.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }))).Next);

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/account/trusted-devices/{device.Id()}")).StatusCode);
        await c.PostOkAsync("/api/auth/logout");
        var again = await app.AnonymousAsync();
        Assert.Equal("mfa", (await StepAsync(await again.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }))).Next);
        var audit = await (await app.AdminAsync()).GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?pageSize=200&entityType=User");
        Assert.Contains(audit.Items, a => a.EntityId == u.Id && a.Action == "TrustedDeviceAdded");
        Assert.Contains(audit.Items, a => a.EntityId == u.Id && a.Action == "TrustedDeviceRevoked");
    }

    [Fact]
    public async Task Revoke_all_trusted_browsers_and_turning_off_needs_password_and_code()
    {
        var u = await EnrolledUserAsync();
        var c = await app.AnonymousAsync();
        var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }));
        await c.PostOkAsync("/api/auth/mfa/verify", new { challenge = step.Challenge, code = TestTotp.Now(u.Key, 0), trustDevice = true });
        await c.RefreshCsrfAsync();
        Assert.Equal(1, (await (await c.PostOkAsync("/api/account/trusted-devices/revoke-all")).JsonAsync()).GetProperty("revoked").GetInt32());
        Assert.Empty((await c.GetElAsync("/api/account/security")).GetProperty("trustedDevices").EnumerateArray());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/account/mfa/disable", new { password = "not-my-password", code = TestTotp.Now(u.Key, 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/account/mfa/disable", new { password = Pw, code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/account/mfa/disable", new { password = Pw, code = TestTotp.Now(u.Key, 1) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode); // this session continues
        Assert.Equal(HttpStatusCode.Unauthorized, (await u.Client.GetAsync("/api/auth/me")).StatusCode); // others end
        var fresh = await app.AnonymousAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await fresh.PostAsync("/api/auth/login", new { email = u.Email, password = Pw })).StatusCode);
        // unlock the failure counter used above
        await (await app.AdminAsync()).PostOkAsync($"/api/admin/users/{u.Id}/unlock");
    }

    [Fact]
    public async Task Recovery_codes_can_be_regenerated_with_a_current_code()
    {
        var u = await EnrolledUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await u.Client.PostAsync("/api/account/mfa/recovery-codes", new { code = u.Codes[0] })).StatusCode); // a recovery code is not enough
        var fresh = (await (await u.Client.PostOkAsync("/api/account/mfa/recovery-codes", new { code = TestTotp.Now(u.Key, 0) })).JsonAsync()).GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(10, fresh.Count);
        Assert.Empty(fresh.Intersect(u.Codes));
        var c = await app.AnonymousAsync();
        var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/auth/mfa/verify", new { challenge = step.Challenge, code = u.Codes[1] })).StatusCode); // old codes are gone
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/mfa/verify", new { challenge = step.Challenge, code = fresh[0] })).StatusCode);
        await (await app.AdminAsync()).PostOkAsync($"/api/admin/users/{u.Id}/unlock");
    }

    [Fact]
    public async Task Wrong_codes_lock_the_account()
    {
        var u = await EnrolledUserAsync();
        var c = await app.AnonymousAsync();
        var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw }));
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++) statuses.Add((await c.PostAsync("/api/auth/mfa/verify", new { challenge = step.Challenge, code = "999999" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);
        Assert.Equal((HttpStatusCode)423, statuses[^1]);
        // logging in again does not reset the counter while a code is still owed
        Assert.Equal((HttpStatusCode)423, (await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw })).StatusCode);
        await (await app.AdminAsync()).PostOkAsync($"/api/admin/users/{u.Id}/unlock");
    }

    [Fact]
    public async Task Temporary_password_must_be_changed_before_anything_else()
    {
        var admin = await app.AdminAsync();
        var email = $"t{Guid.NewGuid():N}@test.local";
        var id = await (await admin.PostOkAsync("/api/admin/users", new { email, displayName = "Temp Person", password = "temporary-pass-778" })).Content.ReadFromJsonAsync<Guid>();
        Assert.True((await admin.GetJsonAsync<List<JsonElement>>("/api/admin/users")).Single(x => x.Id() == id).GetProperty("mustChangePassword").GetBoolean());

        var c = await app.AnonymousAsync();
        var step = await StepAsync(await c.PostAsync("/api/auth/login", new { email, password = "temporary-pass-778" }));
        Assert.Equal("change-password", step.Next);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
        var same = await c.PostAsync("/api/auth/password/change-required", new { challenge = step.Challenge, newPassword = "temporary-pass-778" });
        Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
        Assert.Contains("not used before", await same.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/auth/password/change-required", new { challenge = step.Challenge, newPassword = "short-one" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/password/change-required", new { challenge = step.Challenge, newPassword = "my-own-garden-lamp" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode);
        // the step token cannot be used twice (the password change replaced the security stamp)
        await c.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/auth/password/change-required", new { challenge = step.Challenge, newPassword = "another-garden-lamp" })).StatusCode);

        // an administrator sets a temporary password again: sessions end and the next sign-in asks again
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{id}/temporary-password", new { password = "second-temp-pass-55" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
        var c2 = await app.AnonymousAsync();
        Assert.Equal("change-password", (await StepAsync(await c2.PostAsync("/api/auth/login", new { email, password = "second-temp-pass-55" }))).Next);
        Assert.Equal((HttpStatusCode)403, (await app.CreateClient().PostAsJsonAsync("/api/auth/token", new { email, password = "second-temp-pass-55" })).StatusCode);
        var audit = await admin.GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?pageSize=200&entityType=User");
        Assert.Contains(audit.Items, a => a.EntityId == id && a.Action == "PasswordChanged" && a.UserName == "Temp Person");
        Assert.Contains(audit.Items, a => a.EntityId == id && a.Action == "TemporaryPasswordSet");
    }

    [Fact]
    public async Task Password_rules_common_list_email_name_and_history()
    {
        var (id, c) = await app.CreateUserAsync([Permissions.NcrView]);
        var email = await EmailOf(id);
        async Task<string> Change(string current, string next)
        {
            var r = await c.PostAsync("/api/account/password", new { currentPassword = current, newPassword = next });
            return r.StatusCode == HttpStatusCode.NoContent ? "ok" : await r.Content.ReadAsStringAsync();
        }
        Assert.Contains("current password is not correct", await Change("wrong-password-zz", "a-brand-new-phrase"));
        Assert.Contains("breached or commonly used", await Change(Pw, "Password1234!"));
        Assert.Contains("breached or commonly used", await Change(Pw, "qwertyuiop123"));
        Assert.Contains("email name", await Change(Pw, email.Split('@')[0] + "-x"));
        Assert.Contains("at least 12", await Change(Pw, "tiny-pass"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("four different", await Change(Pw, "aaaaaaaaaaaaaaa"));
        Assert.Equal("ok", await Change(Pw, "first-river-stone"));
        await c.RefreshCsrfAsync();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/auth/me")).StatusCode); // this session continues after the change
        Assert.Equal("ok", await Change("first-river-stone", "second-river-stone"));
        await c.RefreshCsrfAsync();
        Assert.Contains("not used before", await Change("second-river-stone", Pw)); // the original password is in the history
        Assert.Contains("not used before", await Change("second-river-stone", "first-river-stone"));
        Assert.Contains("not used before", await Change("second-river-stone", "second-river-stone"));
        var policy = await (await app.AnonymousAsync()).GetElAsync("/api/auth/password-policy");
        Assert.Equal(12, policy.GetProperty("minLength").GetInt32());
        Assert.Equal(5, policy.GetProperty("historyCount").GetInt32());
        Assert.True(CommonPasswords.Count > 40_000);
        Assert.Contains(TestApp.Mail.To(email), m => m.Subject == "Your password was changed");
    }

    [Fact]
    public async Task Forgot_password_sends_a_single_use_link_without_revealing_accounts_and_ends_sessions()
    {
        var u = await EnrolledUserAsync();
        var anon = await app.AnonymousAsync();
        var unknown = await anon.PostAsync("/api/auth/password/forgot", new { email = $"nobody{Guid.NewGuid():N}@test.local" });
        var known = await anon.PostAsync("/api/auth/password/forgot", new { email = u.Email });
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await known.Content.ReadAsStringAsync());
        var mail = TestApp.Mail.To(u.Email).Single(m => m.Subject == "Reset your password");
        Assert.Contains("/reset-password?token=", mail.Link);
        Assert.Contains("45 minutes", mail.Body);
        var token = mail.Link!.Split("token=")[1];

        Assert.True((await (await anon.PostOkAsync("/api/auth/password/reset/check", new { challenge = token })).JsonAsync()).GetProperty("valid").GetBoolean());
        Assert.Contains("breached", await (await anon.PostAsync("/api/auth/password/reset", new { token, newPassword = "password123456" })).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await anon.PostAsync("/api/auth/password/reset", new { token, newPassword = "reset-copper-kettle" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await u.Client.GetAsync("/api/auth/me")).StatusCode); // existing session ended
        var reuse = await anon.PostAsync("/api/auth/password/reset", new { token, newPassword = "reset-silver-kettle" });
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);
        Assert.Contains("expired or has already been used", await reuse.Content.ReadAsStringAsync());
        Assert.False((await (await anon.PostOkAsync("/api/auth/password/reset/check", new { challenge = token })).JsonAsync()).GetProperty("valid").GetBoolean());
        // the code is still required after a reset
        var c = await app.AnonymousAsync();
        Assert.Equal("mfa", (await StepAsync(await c.PostAsync("/api/auth/login", new { email = u.Email, password = "reset-copper-kettle" }))).Next);
        Assert.Contains(TestApp.Mail.To(u.Email), m => m.Subject == "Your password was changed");

        // a newer link cancels the older one; at most 3 emails an hour per account
        await anon.PostAsync("/api/auth/password/forgot", new { email = u.Email });
        await anon.PostAsync("/api/auth/password/forgot", new { email = u.Email });
        var links = TestApp.Mail.To(u.Email).Where(m => m.Subject == "Reset your password").ToList();
        Assert.Equal(3, links.Count);
        Assert.False((await (await anon.PostOkAsync("/api/auth/password/reset/check", new { challenge = links[1].Link!.Split("token=")[1] })).JsonAsync()).GetProperty("valid").GetBoolean());
        Assert.True((await (await anon.PostOkAsync("/api/auth/password/reset/check", new { challenge = links[2].Link!.Split("token=")[1] })).JsonAsync()).GetProperty("valid").GetBoolean());
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsync("/api/auth/password/forgot", new { email = u.Email })).StatusCode);
        Assert.Equal(3, TestApp.Mail.To(u.Email).Count(m => m.Subject == "Reset your password"));

        var admin = await app.AdminAsync();
        var events = await admin.GetJsonAsync<PageDto<SecurityEventDto>>("/api/security-events?pageSize=200");
        Assert.Contains(events.Items, e => e.EventType == "PasswordResetRequested" && !e.Succeeded && (e.Detail ?? "").Contains("Unknown account"));
        Assert.Contains(events.Items, e => e.EventType == "PasswordResetRequested" && !e.Succeeded && (e.Detail ?? "").Contains("Too many"));
        Assert.Contains(events.Items, e => e.EventType == "PasswordResetCompleted" && e.UserName == u.Email);
        var audit = await admin.GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?pageSize=200&entityType=User");
        Assert.Contains(audit.Items, a => a.EntityId == u.Id && a.Action == "PasswordReset" && a.Description.Contains("all sessions ended"));
    }

    [Fact]
    public async Task Administrator_can_reset_two_step_verification_with_a_reason()
    {
        var u = await EnrolledUserAsync();
        var admin = await app.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/admin/users/{u.Id}/reset-mfa", new { reason = "" })).StatusCode);
        Assert.True((await admin.GetJsonAsync<List<JsonElement>>("/api/admin/users")).Single(x => x.Id() == u.Id).GetProperty("mfaEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{u.Id}/reset-mfa", new { reason = "Lost phone; identity confirmed by video call" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await u.Client.GetAsync("/api/auth/me")).StatusCode);
        var c = await app.AnonymousAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/login", new { email = u.Email, password = Pw })).StatusCode);
        var audit = await admin.GetJsonAsync<PageDto<AuditDto>>("/api/audit-trail?pageSize=200&entityType=User");
        Assert.Contains(audit.Items, a => a.EntityId == u.Id && a.Action == "MfaReset" && a.Description.Contains("Lost phone"));
    }
}
