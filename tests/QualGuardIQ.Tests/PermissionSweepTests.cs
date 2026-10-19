using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace QualGuardIQ.Tests;

/// <summary>
/// Every read endpoint, called by a signed-in person who holds no permissions, returns nothing: no record, list or report data leaks to
/// someone the administrator hasn't given access. Endpoints meant for every signed-in person (their own account, work, notifications,
/// settings and the organization's look) are listed below with the reason.
/// </summary>
[Collection("api")]
public sealed class PermissionSweepTests(TestApp app, ITestOutputHelper output)
{
    static readonly Regex Param = new(@"\{([^}:?]+)[^}]*\}", RegexOptions.Compiled);

    /// <summary>Open to every signed-in person by design.</summary>
    static readonly string[] OpenToEveryone =
    [
        "api/auth/", "api/me", "api/my/", "api/account/", "api/organization/branding", "api/organization/sites/accessible", "api/appearance",
        "api/preferences", "api/notifications", "api/document-types", "api/search", "api/recent", "api/favorites", "api/help",
        "api/organization/settings/public", "api/sign-in-policy", "api/password-policy", "api/deleted-records", "api/my-settings",

        // reference lists the forms and pickers need (names and codes, no record content)
        "api/calibration/equipment-categories", "api/engineering/trace-targets", "api/generation/availability", "api/imports/conversion-targets", "api/imports/targets",
        "api/platform/events/types", "api/platform/modules", "api/platform/my", "api/quality-ops/analytics/modules", "api/quality-ops/settings/intake", "api/quality-ops/analytics/readiness",
        "api/record-types",
        // site and person pickers on every form; portal accounts are refused these
        "api/organization/sites", "api/users/lookup",
    ];

    [Fact]
    public async Task No_read_endpoint_returns_data_to_someone_without_permissions()
    {
        var site = await app.CreateSiteAsync();
        var (_, nobody) = await app.CreateUserAsync([], site);
        var sources = app.Services.GetRequiredService<EndpointDataSource>();
        var gets = sources.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => e.RoutePattern.RawText!.TrimStart('/'))
            .Where(t => t.StartsWith("api/", StringComparison.Ordinal))
            .Distinct().OrderBy(t => t).ToList();
        Assert.True(gets.Count > 200, $"only {gets.Count} GET endpoints found");
        var leaks = new List<string>();
        foreach (var t in gets)
        {
            var bare = t.StartsWith("api/v1/", StringComparison.Ordinal) ? "api/" + t[7..] : t;
            if (OpenToEveryone.Any(o => bare.StartsWith(o, StringComparison.OrdinalIgnoreCase))) continue;
            var url = Param.Replace(t, m => m.Value.Contains(":guid") ? Guid.NewGuid().ToString() : m.Value.Contains(":int") || m.Value.Contains(":long") ? "1" : m.Groups[1].Value == "type" ? "Ncr" : "x");
            var r = await nobody.GetAsync(url);
            if (!r.IsSuccessStatusCode) continue;
            var body = await r.Content.ReadAsStringAsync();
            // an empty answer is no leak (for example an empty list of the person's own items)
            if (body is "" or "[]" or "{}" or "null" || Regex.IsMatch(body, "^\\{\"items\":\\[\\],\"total\":0")) continue;
            leaks.Add($"{url} -> {(int)r.StatusCode} {body[..Math.Min(body.Length, 160)]}");
        }
        foreach (var l in leaks) output.WriteLine(l);
        Assert.True(leaks.Count == 0, "Data returned without permission:\n" + string.Join("\n", leaks));
    }

    static List<string> ReadEndpoints(TestApp app) =>
        app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null)
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => e.RoutePattern.RawText!.TrimStart('/'))
            .Where(t => t.StartsWith("api/", StringComparison.Ordinal) && !t.Contains('{'))
            .Distinct().OrderBy(t => t).ToList();

    /// <summary>
    /// Site restrictions: someone who may view every module at one site sees nothing from another site in any list, report or dashboard
    /// that takes no record id (record pages by id are covered by the record tests).
    /// </summary>
    [Fact]
    public async Task Lists_reports_and_dashboards_never_show_another_sites_records()
    {
        var siteA = await app.CreateSiteAsync();
        var siteB = await app.CreateSiteAsync();
        var tag = "XS" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var views = QualGuardIQ.Domain.Security.Permissions.All.Where(p => p.EndsWith(".view", StringComparison.Ordinal) && !QualGuardIQ.Domain.Security.Permissions.GlobalOnly.Contains(p)).ToArray();
        var (_, owner) = await app.CreateUserAsync([.. views, "ncr.edit", "capa.edit", "suppliers.edit", "calibration.edit", "documents.edit"], siteB);
        await owner.PostOkAsync("/api/ncrs", new { title = $"Crack {tag}", description = tag, source = "In-process", siteId = siteB, detectedOn = TestDates.Today, severity = 0 });
        await owner.PostOkAsync("/api/capas", new { title = $"Drift {tag}", problemStatement = tag, capaType = "Corrective", source = "NCR", siteId = siteB });
        await owner.PostOkAsync("/api/suppliers", new { code = tag[..8], legalName = $"Supplier {tag}", category = "Machining", criticality = 1, risk = 1, siteId = siteB });
        await owner.PostOkAsync("/api/calibration/equipment", new { name = $"Gauge {tag}", siteId = siteB, calibrationIntervalDays = 365, nextDueOn = TestDates.Today.AddDays(-3) });
        await owner.CreateDocumentAsync(siteB, title: $"Procedure {tag}");

        var (_, other) = await app.CreateUserAsync(views, siteA);
        var leaks = new List<string>();
        foreach (var url in ReadEndpoints(app))
        {
            if (url.StartsWith("api/auth/", StringComparison.Ordinal) || url.Contains("/export") || url.EndsWith("/pdf", StringComparison.Ordinal)) continue;
            var r = await other.GetAsync(url);
            if (!r.IsSuccessStatusCode) continue;
            var body = await r.Content.ReadAsStringAsync();
            if (body.Contains(tag, StringComparison.OrdinalIgnoreCase)) leaks.Add(url);
        }
        // the owner does see them, so the check is meaningful
        Assert.Contains(tag, await (await owner.GetAsync($"/api/ncrs?q={tag}")).Content.ReadAsStringAsync());
        foreach (var l in leaks) output.WriteLine(l);
        Assert.True(leaks.Count == 0, "Another site's records were returned by:\n" + string.Join("\n", leaks));
    }
}
