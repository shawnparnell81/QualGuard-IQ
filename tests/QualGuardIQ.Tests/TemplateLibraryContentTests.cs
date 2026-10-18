using System.Text.Json;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Templates;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Enterprise template library content (TECH 4, FI 1 and FI 5): every template loads, validates, carries the common record structure and calculates.</summary>
public class TemplateLibraryContentTests
{
    static readonly DateOnly Today = new(2026, 10, 18);

    [Fact]
    public void Every_template_loads_and_validates()
    {
        var all = TemplateLibrary.Templates;
        Assert.True(all.Count >= 250, $"Only {all.Count} templates loaded.");
        foreach (var t in all)
        {
            FormDefinitions.Validate(t.Definition);
            // the common structure contributes 25 inputs; every template adds at least six of its own
            Assert.True(t.Definition.Fields.Count + t.Definition.Tables.Sum(x => x.Columns.Count) >= 31, $"{t.Key} is too thin.");
        }
        Assert.Equal(all.Count, all.Select(t => t.Key).Distinct().Count());
    }

    [Fact]
    public void Every_template_has_the_common_record_structure()
    {
        string[] sections = ["about", "identification", "related", "approvals", "record_control"];
        string[] fields = ["record_title", "reference_no", "record_status", "site", "department", "owner", "product", "part", "supplier", "customer", "opened_on", "due_on", "closed_on", "supporting_files", "record_retention", "record_audit"];
        foreach (var t in TemplateLibrary.Templates)
        {
            var d = t.Definition;
            foreach (var s in sections) Assert.True(d.Sections.Any(x => x.Key == s), $"{t.Key} lacks section {s}.");
            foreach (var f in fields) Assert.True(d.Fields.Any(x => x.Key == f), $"{t.Key} lacks field {f}.");
            Assert.Contains(d.Tables, x => x.Key == "related_records");
            Assert.NotEmpty(d.Signatures);
            Assert.False(string.IsNullOrWhiteSpace(t.Purpose + t.Scope + t.Instructions + t.Workflow + t.Retention));
        }
    }

    [Fact]
    public void Every_template_calculates_with_empty_input()
    {
        foreach (var t in TemplateLibrary.Templates)
        {
            try { FormDefinitions.Apply(t.Definition, new Dictionary<string, string?>(), false, Today); }
            catch (Exception e) { Assert.Fail($"{t.Key}: {e.Message}"); }
        }
    }

    [Theory]
    [InlineData("NONCONFORMANCE", "Internal Nonconformance Report", "Supplier Nonconformance Report", "Containment Record", "Disposition Authorization", "Scrap Authorization", "Rework Authorization")]
    [InlineData("CAPA", "CAPA Request", "Containment Plan", "Corrective Action Plan", "Preventive Action Plan", "Effectiveness Review", "CAPA Closure")]
    [InlineData("ROOT_CAUSE", "Five Whys", "Fishbone Analysis", "Fault Tree Reference", "Is and Is Not Analysis", "Eight Discipline Problem Solving Package")]
    [InlineData("PPAP", "Part Submission Warrant", "Run at Rate")]
    [InlineData("FAI", "Balloon Characteristic Register", "Balloon Inspection Package", "Delta FAI Record")]
    [InlineData("CALIBRATION", "Calibration Master List", "Variable Gage R&R", "Attribute Gage R&R", "Bias Study", "Linearity Study", "Stability Study")]
    [InlineData("DOCUMENT_CONTROL", "Master Document List", "Master Record List", "Document Change Request")]
    [InlineData("AUDIT", "Audit Plan", "Audit Checklist", "Audit Report", "Layered Process Audit")]
    [InlineData("TRAINING", "Training Matrix", "Competency Matrix", "Skills Matrix")]
    [InlineData("SUPPLIER", "Approved Supplier List", "Supplier Scorecard", "Supplier Corrective Action Request")]
    [InlineData("CUSTOMER_QUALITY", "Complaint Intake", "Return Authorization", "Recall Assessment")]
    [InlineData("RISK_CHANGE", "Risk Register", "Opportunity Register", "Change Request", "Supplier Risk Assessment")]
    [InlineData("MANAGEMENT_REVIEW", "Agenda", "Attendance", "Input Package", "KPI Review", "Audit Review", "Complaint Review", "Supplier Review", "Risk Review", "Resource Review", "Decisions", "Action Register", "Minutes", "Approval")]
    [InlineData("DESIGN_DEVICE", "Design History File Index", "Device Master Record Index", "Device History Record", "Validation Master Plan", "Validation Protocol", "Validation Report")]
    [InlineData("CONFIGURATION", "Configuration Item Register", "Configuration Change Record", "Process Verification Record")]
    public void Required_templates_are_present(string category, params string[] titles)
    {
        var inCat = TemplateLibrary.Templates.Where(t => t.Category == category).Select(t => t.Title).ToList();
        foreach (var title in titles) Assert.True(inCat.Any(x => x.Contains(title, StringComparison.OrdinalIgnoreCase)), $"{category} lacks '{title}'. Has: {string.Join(", ", inCat)}");
    }

    [Fact]
    public void Industry_packs_install_the_families_the_requirements_name()
    {
        var packs = TemplateLibrary.Packs.ToDictionary(p => p.Key);
        Assert.Equal(["aerospace", "automotive", "electronics", "manufacturing", "medical-device"], packs.Keys.OrderBy(k => k));
        string Titles(string k) => string.Join(" | ", TemplateLibrary.PackTemplates(packs[k]).Select(t => t.Title));
        foreach (var w in new[] { "APQP Timing Plan", "Part Submission Warrant", "PFMEA", "Control Plan", "Run at Rate", "Launch Readiness" }) Assert.Contains(w, Titles("automotive"), StringComparison.OrdinalIgnoreCase);
        foreach (var w in new[] { "First Article", "Configuration", "Process Verification" }) Assert.Contains(w, Titles("aerospace"), StringComparison.OrdinalIgnoreCase);
        foreach (var w in new[] { "Design History File", "Device Master Record", "Device History Record", "Validation", "Corrective Action" }) Assert.Contains(w, Titles("medical-device"), StringComparison.OrdinalIgnoreCase);
        foreach (var w in new[] { "Nonconformance", "Corrective Action", "Audit", "Calibration", "Supplier" }) Assert.Contains(w, Titles("manufacturing"), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(TemplateLibrary.PackTemplates(packs["electronics"]));
    }

    [Fact]
    public void Template_text_is_original_and_neutral()
    {
        foreach (var t in TemplateLibrary.Templates)
        {
            var json = FormDefinitions.Serialize(t.Definition) + t.Title + t.Purpose + t.Scope + t.Instructions;
            foreach (var bad in new[] { "prior product", "prior product", "lorem", "TODO", "TBD" }) Assert.DoesNotContain(bad, json);
        }
    }

    static Dictionary<string, string?> Run(string key, Dictionary<string, string?> input) => FormDefinitions.Apply(TemplateLibrary.Find(key)!.Definition, input, false, Today);
    static string Rows(params object[] rows) => JsonSerializer.Serialize(new { rows });

    [Fact]
    public void Device_history_record_reconciles_and_counts_failed_steps()
    {
        var o = Run("device-history-record", new()
        {
            ["dhr_started"] = "100", ["dhr_released"] = "95", ["dhr_rejected"] = "3", ["dhr_samples"] = "1",
            ["dhr_step_rows"] = Rows(new { dhs_step = "Mould", dhs_result = "pass" }, new { dhs_step = "Seal", dhs_result = "fail" }),
        });
        Assert.Equal("1", o["dhr_difference"]);
        Assert.Equal("95", o["dhr_yield"]?.TrimEnd('0').TrimEnd('.'));
        Assert.Equal("1", o["dhr_failed_steps"]);
    }

    [Fact]
    public void Configuration_change_is_classified_major_when_form_fit_or_function_is_affected()
    {
        Assert.Equal("Minor", Run("configuration-change-record", new())["ccr_class"]);
        Assert.Equal("Major", Run("configuration-change-record", new() { ["ccr_fit"] = "true" })["ccr_class"]);
    }

    [Fact]
    public void Process_verification_flags_parameters_out_of_limits()
    {
        var o = Run("process-verification-record", new()
        {
            ["pvr_param_rows"] = Rows(new { pp_parameter = "Temperature", pp_low = "840", pp_high = "860", pp_actual = "855" }, new { pp_parameter = "Time", pp_low = "50", pp_high = "70", pp_actual = "75" }),
        });
        Assert.Equal("1", o["pvr_out"]);
    }

    [Fact]
    public void Kpi_review_works_out_target_and_trend()
    {
        var o = Run("mr-kpi-review", new()
        {
            ["kpi_rows"] = Rows(new { kpi_name = "On-time delivery", kpi_target = "95", kpi_actual = "92", kpi_previous = "94", kpi_higher_better = "true" },
                                new { kpi_name = "PPM", kpi_target = "50", kpi_actual = "40", kpi_previous = "60", kpi_higher_better = "false" }),
        });
        Assert.Equal("1", o["kpi_missed"]);
        Assert.Equal("1", o["kpi_worse"]);
    }

    [Fact]
    public void Validation_master_plan_tracks_stage_progress()
    {
        var o = Run("validation-master-plan", new()
        {
            ["vmp_rows"] = Rows(new { vm_item = "Sealer", vm_type = "Process", vm_iq = "true", vm_oq = "true", vm_pq = "true", vm_report = "true" },
                                new { vm_item = "Oven", vm_type = "Equipment", vm_iq = "true" }),
        });
        Assert.Equal("1", o["vmp_complete"]);
        Assert.Equal("2", o["vmp_count"]);
    }

    [Fact]
    public void Management_review_action_register_flags_overdue_actions()
    {
        var o = Run("mr-action-register", new()
        {
            ["mra_as_of"] = "2026-10-18",
            ["mact_rows"] = Rows(new { mx_review = "Q2", mx_action = "Add capacity", mx_due = "2026-09-30" },
                                 new { mx_review = "Q2", mx_action = "Train inspectors", mx_due = "2026-11-30" },
                                 new { mx_review = "Q1", mx_action = "Close audit actions", mx_due = "2026-06-30", mx_done = "2026-06-15" }),
        });
        Assert.Equal("1", o["mx_overdue"]);
        Assert.Equal("1", o["mx_open"]);
    }
}
