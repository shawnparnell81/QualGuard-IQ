using System.Text.Json;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Templates;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>The PPAP set, the automotive additions (validation, first article, first pass yield) and the electronics pack.</summary>
public sealed class PpapSetTests
{
    static readonly DateOnly Today = new(2026, 10, 18);
    static Dictionary<string, string?> Run(string key, Dictionary<string, string?> input) => FormDefinitions.Apply(TemplateLibrary.Find(key)!.Definition, input, false, Today);
    static string Rows(params object[] rows) => JsonSerializer.Serialize(new { rows });
    static List<JsonElement> RowsOf(Dictionary<string, string?> o, string key) => JsonDocument.Parse(o[key]!).RootElement.GetProperty("rows").EnumerateArray().ToList();
    static decimal Num(string? s) => decimal.Parse(s!, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Automotive_pack_covers_every_ppap_function()
    {
        var keys = TemplateLibrary.PackTemplates(TemplateLibrary.Packs.Single(p => p.Key == "automotive")).Select(t => t.Key).ToHashSet();
        string[] functions =
        [
            "ppap-common-data", "ppap-characteristics-list", "ppap-warrant", "ppap-truck-warrant", "ppap-appearance-approval", "ppap-dimensional-results", "ppap-material-results",
            "ppap-performance-results", "ppap-bulk-material-checklist", "ppap-design-matrix", "supplier-change-notification", "apqp-control-plan", "apqp-pfmea", "apqp-process-flow",
            "ppap-inspection-report", "ppap-appearance-report", "ppap-element-checklist", "supplier-deviation-request", "ppap-interim-recovery", "ppap-engineering-source-approval",
            "ppap-bulk-interim-approval", "ppap-process-matrix", "ppap-pqp-summary", "ppap-data-point-coordinates", "apqp-feasibility-review", "eight-d-package",
            "ncr-log", "product-validation-report", "automotive-first-article", "first-pass-yield",
        ];
        foreach (var k in functions) Assert.True(keys.Contains(k), $"automotive lacks {k}.");
    }

    [Fact]
    public void Submission_forms_take_their_rows_from_the_characteristics_list()
    {
        Assert.Equal("characteristics?char_type=Dimensional", TemplateLibrary.Find("ppap-dimensional-results")!.Definition.Tables.Single(t => t.Key == "dim_rows").Prefill);
        Assert.Equal("characteristics?char_type=Material", TemplateLibrary.Find("ppap-material-results")!.Definition.Tables.Single(t => t.Key == "mat_rows").Prefill);
        var cp = TemplateLibrary.Find("apqp-control-plan")!.Definition.Tables.Single(t => t.Key == "cp_rows");
        Assert.Equal("characteristics", cp.Prefill);
        Assert.Contains(cp.Columns, c => c.Key == "cp_lsl" && c.Prefill == "char_lsl");
        Assert.Contains(cp.Columns, c => c.Key == "cp_usl" && c.Prefill == "char_usl");
        Assert.Equal("org_name", TemplateLibrary.Find("ppap-warrant")!.Definition.Fields.Single(f => f.Key == "psw_org").Prefill);
        // the common data page and the forms it feeds share the part block by key
        foreach (var k in new[] { "ppap-common-data", "ppap-warrant", "ppap-truck-warrant", "ppap-dimensional-results", "ppap-inspection-report" })
            Assert.Contains(TemplateLibrary.Find(k)!.Definition.Fields, f => f.Key == "customer_part_number");
        Assert.Contains("Transport", TemplateLibrary.Find("apqp-process-flow")!.Definition.Tables.Single(t => t.Key == "flow_steps").Columns.Single(c => c.Key == "step_type").Options!);
    }

    [Fact]
    public void First_pass_yield_and_rolled_throughput_yield_are_calculated()
    {
        var o = Run("first-pass-yield", new()
        {
            ["fpy_target"] = "85",
            ["fpy_rows"] = Rows(new { op_no = "10", op_in = "200", op_good = "190", op_rework = "8", op_scrap = "2" }, new { op_no = "20", op_in = "198", op_good = "180", op_rework = "18", op_scrap = "0" }),
        });
        var rows = RowsOf(o, "fpy_rows");
        Assert.Equal(95m, Num(rows[0].GetProperty("op_fpy").ToString()));
        Assert.Equal(0m, Num(rows[0].GetProperty("op_balance").ToString()));
        // 0.95 x 0.909090... = 86.36 %
        Assert.Equal(86.36m, Math.Round(Num(o["fpy_rty"]), 2));
        Assert.Equal("Yes", o["fpy_meets"]);
    }

    [Fact]
    public void Validation_report_decides_each_test()
    {
        var o = Run("product-validation-report", new() { ["pvr_rows"] = Rows(
            new { pvr_test = "Salt spray", pvr_criterion = "No red rust", pvr_planned = "6", pvr_tested = "6", pvr_passed = "6" },
            new { pvr_test = "Vibration", pvr_criterion = "No cracks", pvr_planned = "6", pvr_tested = "6", pvr_passed = "5" },
            new { pvr_test = "Thermal cycle", pvr_criterion = "Function", pvr_planned = "6", pvr_tested = "3", pvr_passed = "3" }) });
        Assert.Equal(["PASS", "FAIL", "OPEN"], RowsOf(o, "pvr_rows").Select(r => r.GetProperty("pvr_result").GetString()));
        Assert.Equal("No", o["pvr_validated"]);
    }

    [Fact]
    public void Coordinates_inspection_and_appearance_are_measured_against_limits()
    {
        var d = Run("ppap-data-point-coordinates", new() { ["dpc_rows"] = Rows(new { dpc_id = "P1", dpc_nx = "0", dpc_ny = "0", dpc_nz = "0", dpc_mx = "0.3", dpc_my = "0.4", dpc_mz = "0", dpc_tol = "0.5" },
            new { dpc_id = "P2", dpc_nx = "10", dpc_ny = "0", dpc_nz = "0", dpc_mx = "10.6", dpc_my = "0", dpc_mz = "0", dpc_tol = "0.5" }) });
        var pts = RowsOf(d, "dpc_rows");
        Assert.Equal(0.5m, Num(pts[0].GetProperty("dpc_total").ToString()));
        Assert.Equal("OK", pts[0].GetProperty("dpc_ok").GetString());
        Assert.Equal("1", d["dpc_not_ok"]);

        var ir = Run("ppap-inspection-report", new() { ["ir_rows"] = Rows(new { ir_no = "1", ir_char = "Bore", ir_lsl = "9.95", ir_usl = "10.05", ir_s1 = "10.01", ir_s2 = "10.06" }) });
        Assert.Equal("NOT OK", RowsOf(ir, "ir_rows")[0].GetProperty("ir_ok").GetString());
        Assert.Equal("Reject", ir["ir_decision"]);

        var ae = Run("ppap-appearance-report", new() { ["aer_tolerance"] = "1", ["aer_rows"] = Rows(new { aer_sample = "S1", aer_m_l = "50", aer_m_a = "0", aer_m_b = "0", aer_s_l = "50.6", aer_s_a = "0.8", aer_s_b = "0" }) });
        Assert.Equal(1m, Num(RowsOf(ae, "aer_rows")[0].GetProperty("aer_de").ToString()));
    }

    [Fact]
    public void Electronics_forms_calculate_floor_life_and_defect_rates()
    {
        var msd = Run("msd-floor-life-log", new() { ["msd_rows"] = Rows(new { msd_part = "U1", msd_lot = "R1", msd_level = "3", msd_hours = "170" }, new { msd_part = "U2", msd_lot = "R2", msd_level = "4", msd_hours = "10" }) });
        Assert.Equal("1", msd["msd_expired"]);
        var pw = Run("pcba-workmanship-inspection", new() { ["pwi_boards"] = "100", ["pwi_opportunities"] = "500", ["pwi_boards_good"] = "97", ["pwi_rows"] = JsonSerializer.Serialize(new { rows = new[] { new { pwi_count = "3" }, new { pwi_count = "2" } } }) });
        Assert.Equal(100m, Num(pw["pwi_dpmo"]));
        Assert.Equal(97m, Num(pw["pwi_yield"]));
    }
}
