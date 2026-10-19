using System.Text;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Templates;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Form looks: each family of library forms gets its own layout while fields, rules and signatures behave the same.</summary>
public sealed class FormLayoutThemeTests
{
    static FormPrintContext Ctx() => new("Acme Precision", "Layout check", "FT-0002", "A", "FR-000007", "Draft", "S1 Plant", "2026-10-18 09:00 UTC-04:00", "Pat Lee", "Internal", null, null, null);

    [Theory]
    [InlineData("ppap-warrant", "PPAP", "certificate")]
    [InlineData("calibration-schedule", "CALIBRATION", "register")]
    [InlineData("layered-process-audit-checklist", "AUDIT", "checklist")]
    [InlineData("ppap-dimensional-results", "PPAP", "worksheet")]
    [InlineData("ncr-report", "NONCONFORMANCE", "investigation")]
    [InlineData("apqp-phase-plan", "APQP", "plan")]
    [InlineData("supplier-scorecard", "SUPPLIER", "report")]
    public void Library_forms_get_a_look_from_what_they_are(string key, string category, string theme) => Assert.Equal(theme, TemplateLibrary.DefaultTheme(key, category));

    [Fact]
    public void The_library_uses_every_look_and_each_form_has_a_valid_one()
    {
        var themes = TemplateLibrary.Templates.Select(t => FormThemes.Of(t.Definition)).ToHashSet();
        foreach (var th in FormThemes.Themes) Assert.Contains(th, themes);
        foreach (var t in TemplateLibrary.Templates)
        {
            Assert.Contains(t.Definition.Page.Theme, FormThemes.Themes);
            // behaviour stays the same: every form keeps the common sections whatever it looks like
            Assert.Contains(t.Definition.Sections, s => s.Key == "approvals" && s.Style == "signoff");
        }
    }

    [Fact]
    public void Unknown_looks_and_section_styles_are_refused()
    {
        var d = new FormDefinition { Page = new PageSetup { Theme = "poster" }, Sections = [new FormSection { Key = "a", Title = "A" }], Fields = [new FormField { Key = "x", Label = "X", Type = "text", Section = "a" }] };
        Assert.Contains("Form look", Assert.ThrowsAny<Exception>(() => FormDefinitions.Validate(d)).Message);
        d.Page.Theme = "worksheet"; d.Sections[0].Style = "neon";
        Assert.Contains("style must be one of", Assert.ThrowsAny<Exception>(() => FormDefinitions.Validate(d)).Message);
        d.Sections[0].Style = "titleblock";
        FormDefinitions.Validate(d);
    }

    [Fact]
    public void Each_look_prints_differently_and_keeps_values_and_signatures()
    {
        var outputs = new Dictionary<string, string>();
        foreach (var th in FormThemes.Themes)
        {
            var d = new FormDefinition
            {
                Page = new PageSetup { Theme = th },
                Sections = [new FormSection { Key = "head", Title = "Header" }, new FormSection { Key = "sign", Title = "Sign-off", Style = "signoff" }],
                Fields = [new FormField { Key = "part", Label = "Part number", Type = "text", Section = "head" }],
                Tables = [new FormTable { Key = "rows", Title = "Results", Section = "head", Columns = [new FormField { Key = "c", Label = "Characteristic", Type = "text" }] }],
                Signatures = [new SignatureBlock { Key = "ok", Label = "Approved by", Role = "approved", Meaning = "I approve", Section = "sign" }],
            };
            FormDefinitions.Validate(d);
            var pdf = Encoding.Latin1.GetString(new FormPdfRenderer(d, new Dictionary<string, string?> { ["part"] = "PN-778" }, [new PrintedSignature("ok", "Dana Reyes", "2026-10-18 09:10", "I approve", "Password", true)], Ctx()).Render("standardized"));
            Assert.Contains("(PN-778) Tj", pdf);
            Assert.Contains("(Dana Reyes) Tj", pdf);
            outputs[th] = System.Text.RegularExpressions.Regex.Replace(pdf, @"/CreationDate \(.*?\)|/ID \[.*?\]", "");
        }
        Assert.Contains("(1   HEADER) Tj", outputs["worksheet"]);
        Assert.Contains("(1  HEADER) Tj", outputs["register"]);
        Assert.Contains("(1  Header) Tj", outputs["report"]);
        Assert.Contains("(Name / signature) Tj", outputs["certificate"]);   // boxed signature panel
        Assert.DoesNotContain("(Name / signature) Tj", outputs["investigation"]); // ruled signature lines
        Assert.Equal(FormThemes.Themes.Length, outputs.Values.Distinct().Count());
    }
}
