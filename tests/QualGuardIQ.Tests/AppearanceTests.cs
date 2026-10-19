using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Appearance;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Settings › Appearance: organization default by permission and audited, a personal override per person, and printed forms in the family palette.</summary>
[Collection("api")]
public sealed class AppearanceTests(TestApp app)
{
    private sealed record Pal(string Palette, string? Edge = null, string? Accent = null);
    private sealed record Values(string? Mode, string? PrimaryColor, string? AccentColor, Dictionary<string, Pal>? FormPalettes);
    private sealed record Info(Values? Organization, Values? Personal, bool CanEditOrganization, DefaultsDto Defaults);
    private sealed record DefaultsDto(string Mode, string PrimaryColor, string AccentColor, Dictionary<string, Pal> FormPalettes);

    [Fact]
    public async Task Organization_default_needs_the_permission_and_is_audited_and_people_keep_their_own()
    {
        var admin = await app.AdminAsync();
        var (_, plain) = await app.CreateUserAsync([Permissions.NcrView]);
        try
        {
            var before = await plain.GetJsonAsync<Info>("/api/appearance");
            Assert.False(before.CanEditOrganization);
            Assert.Equal("palette1", before.Defaults.FormPalettes["checklist"].Palette);
            Assert.Equal("palette2", before.Defaults.FormPalettes["certificate"].Palette);
            Assert.Equal("#0E8550", before.Defaults.AccentColor);

            var org = new Values("dark", "#1257C4", "#0E8550", new() { ["certificate"] = new Pal("custom", "#123456", "#ABCDEF"), ["checklist"] = new Pal("palette2") });
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.PutAsync("/api/appearance/organization", org)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/appearance/organization", org)).StatusCode);

            // a person's own choice does not touch the organization default
            Assert.Equal(HttpStatusCode.NoContent, (await plain.PutAsync("/api/appearance/me", new Values("light", null, "#AA3300", null))).StatusCode);
            var seen = await plain.GetJsonAsync<Info>("/api/appearance");
            Assert.Equal("dark", seen.Organization!.Mode);
            Assert.Equal("#123456", seen.Organization.FormPalettes!["certificate"].Edge);
            Assert.Equal("light", seen.Personal!.Mode);
            Assert.Equal("#AA3300", seen.Personal.AccentColor);
            Assert.Null((await admin.GetJsonAsync<Info>("/api/appearance")).Personal);

            // bad values are refused
            Assert.Equal(HttpStatusCode.BadRequest, (await plain.PutAsync("/api/appearance/me", new Values("sepia", null, null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await plain.PutAsync("/api/appearance/me", new Values(null, "blue", null, null))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await plain.PutAsync("/api/appearance/me", new Values(null, null, null, new() { ["plan"] = new Pal("custom", "#123456") }))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await plain.PutAsync("/api/appearance/me", new Values(null, null, null, new() { ["poster"] = new Pal("palette1") }))).StatusCode);

            // who, what, when and a readable description for the organization change
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var audit = await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == "AppearanceSetting" && a.Description.Contains("Organization appearance"))
                .OrderByDescending(a => a.OccurredAt).FirstAsync();
            Assert.Equal("Administrator", audit.UserName);
            Assert.True(audit.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(-5));
            Assert.Contains("Organization appearance", audit.Description);

            // printed certificates use the organization's palette for the family
            Assert.Equal(("#123456", "#ABCDEF"), await AppearanceModel.PrintColoursAsync(db, "certificate", default));
            Assert.Equal(AppearanceModel.Colours("palette2"), await AppearanceModel.PrintColoursAsync(db, "checklist", default));
            Assert.Equal(AppearanceModel.Colours("palette1"), await AppearanceModel.PrintColoursAsync(db, "plan", default));
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/appearance/organization", new Values(null, null, null, null))).StatusCode);
        }
        var reset = await admin.GetJsonAsync<Info>("/api/appearance");
        Assert.Null(reset.Organization!.Mode);
        Assert.Empty(reset.Organization.FormPalettes!);
    }

    [Fact]
    public void Built_in_palettes_split_the_families_between_green_and_blue_and_slate_and_blue()
    {
        foreach (var f in new[] { "checklist", "investigation", "plan" }) Assert.Equal("palette1", AppearanceModel.DefaultPalette(f));
        foreach (var f in new[] { "certificate", "report", "worksheet", "register" }) Assert.Equal("palette2", AppearanceModel.DefaultPalette(f));
        Assert.Equal(FormThemes.Themes.Length, AppearanceModel.DefaultFormPalettes().Count);
        Assert.Equal(("#0E8550", "#1257C4"), AppearanceModel.Colours("palette1"));
        Assert.Equal(("#334155", "#1257C4"), AppearanceModel.Colours("palette2"));
    }

    [Fact]
    public void Certificate_pdfs_print_in_their_palette_with_a_frame()
    {
        var d = new FormDefinition { Page = new PageSetup { Theme = "certificate" } };
        d.Sections.Add(new FormSection { Key = "a", Title = "Part" });
        d.Fields.Add(new FormField { Key = "part", Label = "Part number", Type = "text", Section = "a" });
        var ctx = new FormPrintContext("Org", "Warrant", "FT-1", "A", "R-1", "Draft", null, "2026-10-19", "Tester", "Internal", null, null, null);
        var pdf = System.Text.Encoding.Latin1.GetString(new FormPdfRenderer(d, new Dictionary<string, string?>(), [], ctx) { Palette = ("#123456", "#ABCDEF") }.Render("standardized"));
        // #123456 as a stroke colour (frame and rules) and #ABCDEF for the title
        Assert.Contains("0.071 0.204 0.337 RG", pdf);
        Assert.Contains("0.671 0.804 0.937 rg", pdf);
    }
}
