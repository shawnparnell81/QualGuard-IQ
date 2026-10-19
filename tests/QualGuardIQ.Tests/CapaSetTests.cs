using System.Text.Json;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Templates;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>The CAPA set (report, action plan, root cause worksheet, effectiveness check and log) that every industry pack installs.</summary>
public sealed class CapaSetTests
{
    static readonly DateOnly Today = new(2026, 10, 18);
    static readonly string[] Set = ["capa-report", "capa-action-plan", "capa-root-cause-worksheet", "capa-effectiveness-check", "capa-log"];
    static Dictionary<string, string?> Run(string key, Dictionary<string, string?> input) => FormDefinitions.Apply(TemplateLibrary.Find(key)!.Definition, input, false, Today);
    static string Rows(params object[] rows) => JsonSerializer.Serialize(new { rows });
    static FormDefinition Def(string key) => TemplateLibrary.Find(key)!.Definition;

    [Fact]
    public void Every_pack_installs_the_whole_capa_set()
    {
        foreach (var p in TemplateLibrary.Packs)
        {
            var keys = TemplateLibrary.PackTemplates(p).Select(t => t.Key).ToHashSet();
            foreach (var k in Set) Assert.True(keys.Contains(k), $"{p.Key} lacks {k}.");
            // one CAPA form per pack: the older request/plan/closure forms stay in the library but are not installed by packs
            Assert.DoesNotContain("capa-request", keys);
            Assert.DoesNotContain("capa-closure", keys);
        }
    }

    [Fact]
    public void Report_follows_the_nine_parts_in_order()
    {
        var d = Def("capa-report");
        var titles = d.Sections.Where(s => s.Parent is null).Select(s => s.Title).ToList();
        string[] expected = ["CAPA identification and control", "Problem / potential problem description", "Initial risk and impact assessment", "Immediate correction and containment",
            "Investigation and root cause analysis", "Action plan summary", "Change control, communication, training, and validation", "Effectiveness verification and closure", "Final disposition"];
        var at = expected.Select(t => titles.IndexOf(t)).ToList();
        Assert.DoesNotContain(-1, at);
        Assert.Equal(at.Order(), at);
        Assert.Equal(["CAPA number", "Date opened", "Target closure date", "CAPA type", "Priority / risk", "Status"], d.Fields.Where(f => f.Section == "identification").Take(6).Select(f => f.Label));
        Assert.Equal(["Draft", "Open", "Investigation", "Action in Progress", "Pending Verification", "Effectiveness Monitoring", "Closed", "Cancelled"], d.Fields.Single(f => f.Key == "record_status").Options);
        Assert.Equal(11, d.Fields.Single(f => f.Key == "source_trigger").Options!.Count);
        Assert.Equal(["Prepared / owner", "Quality review", "Process owner", "Management approval"], d.Signatures.Select(s => s.Label));
    }

    [Fact]
    public void Report_scores_initial_and_residual_risk()
    {
        var o = Run("capa-report", new() { ["initial_severity"] = "4", ["initial_occurrence"] = "3", ["initial_detection"] = "5", ["residual_severity"] = "4", ["residual_occurrence"] = "1" });
        Assert.Equal("60", o["initial_rpn"]);
        Assert.True(string.IsNullOrEmpty(o["residual_rpn"]));
        Assert.Equal("8", Run("capa-report", new() { ["residual_severity"] = "4", ["residual_occurrence"] = "1", ["residual_detection"] = "2" })["residual_rpn"]);
    }

    [Fact]
    public void Action_plan_counts_open_actions()
    {
        var o = Run("capa-action-plan", new() { ["plan_actions"] = Rows(
            new { action_id = "A1", action_status = "Closed" }, new { action_id = "A2", action_status = "Action in Progress" }, new { action_id = "A3", action_status = "Cancelled" }) });
        Assert.Equal("3", o["actions_count"]);
        Assert.Equal("1", o["actions_closed"]);
        Assert.Equal("1", o["actions_open"]);
    }

    [Fact]
    public void Root_cause_worksheet_has_five_whys_and_six_categories()
    {
        var d = Def("capa-root-cause-worksheet");
        Assert.Equal(["Why 1", "Why 2", "Why 3", "Why 4", "Why 5"], d.Tables.Single(t => t.Key == "whys").FixedRows);
        Assert.Equal(["People", "Methods", "Machines / equipment", "Materials", "Measurement", "Environment"], d.Tables.Single(t => t.Key == "cause_categories").Columns.Select(c => c.Label));
    }

    [Fact]
    public void Effectiveness_check_counts_failed_checks()
    {
        var o = Run("capa-effectiveness-check", new() { ["eff_checks"] = Rows(new { chk_metric = "Escapes", chk_result = "pass" }, new { chk_metric = "Scrap", chk_result = "fail" }) });
        Assert.Equal("1", o["eff_failed"]);
    }

    [Fact]
    public void Log_counts_days_open_and_flags_overdue_capas()
    {
        var o = Run("capa-log", new() { ["capa_entries"] = Rows(
            new { log_number = "CAPA-1", log_opened = "2026-10-01", log_target = "2026-10-10", log_status = "Open" },
            new { log_number = "CAPA-2", log_opened = "2026-09-01", log_actual = "2026-09-21", log_target = "2026-09-30", log_status = "Closed" },
            new { log_number = "CAPA-3", log_opened = "2026-10-15", log_target = "2026-11-15", log_status = "Investigation" }) });
        var rows = JsonDocument.Parse(o["capa_entries"]!).RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal("17", rows[0].GetProperty("log_days_open").ToString());
        Assert.Equal("YES", rows[0].GetProperty("log_overdue").GetString());
        Assert.Equal("20", rows[1].GetProperty("log_days_open").ToString());
        Assert.True(string.IsNullOrEmpty(rows[1].GetProperty("log_overdue").GetString()));
        Assert.Equal("NO", rows[2].GetProperty("log_overdue").GetString());
        Assert.Equal("2", o["log_open_count"]);
        Assert.Equal("1", o["log_overdue_count"]);
    }
}
