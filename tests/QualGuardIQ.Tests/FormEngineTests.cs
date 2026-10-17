using System.Text.Json.Nodes;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Form schema version 2 and the form formula engine (no database).</summary>
public class FormEngineTests
{
    static FValue Eval(string f, Dictionary<string, FValue>? vars = null) =>
        FormulaEngine.Evaluate(f, n => vars is not null && vars.TryGetValue(n, out var v) ? v : null, new DateOnly(2026, 10, 17));
    static decimal Num(string f, Dictionary<string, FValue>? vars = null) => Eval(f, vars).AsNumber()!.Value;
    static FValue L(params decimal[] xs) => FValue.List(xs.Select(FValue.Num).ToList());

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("2 ^ 3 ^ 2", 512)]
    [InlineData("-2 ^ 2", 4)]
    [InlineData("50%", 0.5)]
    [InlineData("ROUND(2.345, 2)", 2.35)]
    [InlineData("ROUNDUP(2.341, 2)", 2.35)]
    [InlineData("ROUNDDOWN(2.349, 2)", 2.34)]
    [InlineData("MOD(-7, 3)", 2)]
    [InlineData("ABS(-4) + SQRT(16)", 8)]
    [InlineData("IF(3 > 2, 10, 20)", 10)]
    [InlineData("IFS(1 = 2, 1, 2 = 2, 5)", 5)]
    [InlineData("SWITCH(\"b\", \"a\", 1, \"b\", 2, 9)", 2)]
    [InlineData("LEN(\"abc\" & \"de\")", 5)]
    [InlineData("DAYS(\"2026-10-17\", \"2026-10-01\")", 16)]
    [InlineData("MONTH(EDATE(\"2026-01-31\", 1))", 2)]
    [InlineData("CEILING(7, 5) + FLOOR(7, 5)", 15)]
    public void Arithmetic_and_functions(string formula, double expected) => Assert.Equal((decimal)expected, Num(formula));

    [Fact]
    public void Aggregates_over_table_columns()
    {
        var v = new Dictionary<string, FValue> { ["t.x"] = L(2, 4, 4, 4, 5, 5, 7, 9), ["t.r"] = FValue.List([FValue.Text("pass"), FValue.Text("fail"), FValue.Text("pass")]) };
        Assert.Equal(40, Num("SUM(t.x)", v));
        Assert.Equal(5, Num("AVERAGE(t.x)", v));
        Assert.Equal(4.5m, Num("MEDIAN(t.x)", v));
        Assert.Equal(7, Num("RANGE(t.x)", v));
        Assert.Equal(2.138m, Math.Round(Num("STDEV(t.x)", v), 3));
        Assert.Equal(2m, Num("STDEVP(t.x)", v));
        Assert.Equal(2, Num("COUNTIF(t.r, \"pass\")", v));
        Assert.Equal(4, Num("COUNTIF(t.x, \">=5\")", v));
        Assert.Equal(26, Num("SUMIF(t.x, \">4\")", v));
    }

    [Fact]
    public void Capability_and_tolerance_helpers()
    {
        var v = new Dictionary<string, FValue> { ["d"] = L(9.9m, 10m, 10.1m, 10m, 9.95m, 10.05m), ["m"] = FValue.Num(10.2m) };
        Assert.True(Num("CP(d, 9.5, 10.5)", v) > 1.3m);
        Assert.True(Num("CPK(d, 9.5, 10.5)", v) > 1.3m);
        Assert.False(Eval("WITHIN(m, 9.9, 10.1)", v).AsBool());
        Assert.True(Eval("WITHIN(10, 9.9, 10.1)").AsBool());
        Assert.True(Eval("WITHIN(m, 9.9, 10.1)", v).Raw is bool);
    }

    [Fact]
    public void Errors_are_values_not_exceptions()
    {
        Assert.Equal("#DIV/0!", Eval("1 / 0").AsText());
        Assert.Equal("#VALUE!", Eval("\"a\" * 2").AsText());
        Assert.Equal(7, Num("IFERROR(1 / 0, 7)"));
        Assert.True(Eval("1 + x", new() { ["x"] = FValue.Empty }).IsEmpty);
    }

    [Theory]
    [InlineData("SHELL(\"rm\")", "not supported")]
    [InlineData("SUM(1,", "ends unexpectedly")]
    [InlineData("\"open", "closing quote")]
    [InlineData("ROUND()", "argument")]
    [InlineData("1 $ 2", "invalid character")]
    public void Unsafe_or_broken_formulas_are_rejected(string formula, string message)
    {
        var e = Assert.Throws<AppException>(() => FormulaEngine.Parse(formula));
        Assert.Contains(message, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formula_length_and_depth_are_limited()
    {
        Assert.Throws<AppException>(() => FormulaEngine.Parse(new string('1', 2001)));
        Assert.Throws<AppException>(() => FormulaEngine.Parse(new string('(', 200) + "1" + new string(')', 200)));
    }

    const string Inspection = """
    {
      "schemaVersion": 2,
      "page": { "size": "Letter", "orientation": "portrait", "header": { "left": "{template} rev {revision}", "right": "Page {page} of {pages}" } },
      "sections": [
        { "key": "header", "title": "Record identification", "columns": 3 },
        { "key": "results", "title": "Dimensional results", "orientation": "landscape" },
        { "key": "failure", "title": "Failure details", "visibleWhen": { "field": "outcome", "operator": "equals", "value": "Fail" } },
        { "key": "ops", "title": "Operations", "repeatable": true, "minRepeat": 1, "maxRepeat": 5, "repeatLabel": "Operation" }
      ],
      "fields": [
        { "key": "lot", "label": "Lot", "type": "text", "required": true, "section": "header" },
        { "key": "qty", "label": "Quantity", "type": "integer", "min": 1, "section": "header" },
        { "key": "price", "label": "Unit cost", "type": "currency", "decimals": 2, "section": "header" },
        { "key": "outcome", "label": "Outcome", "type": "radio", "options": ["Pass", "Fail"], "section": "header" },
        { "key": "reason", "label": "Reason", "type": "longtext", "required": true, "section": "failure" },
        { "key": "op_no", "label": "Operation number", "type": "integer", "required": true, "section": "ops" },
        { "key": "op_minutes", "label": "Minutes", "type": "number", "section": "ops" },
        { "key": "op_cost", "label": "Cost", "type": "calc", "formula": "op_minutes * 2", "section": "ops" },
        { "key": "total_minutes", "label": "Total minutes", "type": "calc", "formula": "SUM(ops.op_minutes)", "section": "header" },
        { "key": "value", "label": "Lot value", "type": "calc", "formula": "qty * price", "decimals": 2, "section": "header" },
        { "key": "worst", "label": "Largest deviation", "type": "calc", "formula": "MAX(dims.dev_abs)", "decimals": 3, "section": "results" },
        { "key": "fails", "label": "Failed characteristics", "type": "calc", "formula": "COUNTIF(dims.result, \"FAIL\")", "section": "results" }
      ],
      "tables": [
        {
          "key": "dims", "title": "Characteristics", "section": "results", "minRows": 1, "maxRows": 50, "subtotalBy": "zone",
          "headerGroups": [ { "label": "Characteristic", "span": 2 }, { "label": "Specification", "span": 3 }, { "label": "Result", "span": 4 } ],
          "columns": [
            { "key": "balloon", "label": "Balloon", "type": "integer", "required": true },
            { "key": "zone", "label": "Zone", "type": "choice", "options": ["A", "B"] },
            { "key": "nominal", "label": "Nominal", "type": "number" },
            { "key": "lsl", "label": "Lower limit", "type": "number" },
            { "key": "usl", "label": "Upper limit", "type": "number" },
            { "key": "measured", "label": "Measured", "type": "measurement", "nominal": 0 },
            { "key": "dev_abs", "label": "Deviation", "type": "calc", "formula": "ABS(measured - nominal)", "decimals": 3 },
            { "key": "result", "label": "Result", "type": "calc", "formula": "WITHIN(measured, lsl, usl)", "resultLabels": ["PASS", "FAIL"] },
            { "key": "gauges", "label": "Gauges", "type": "table", "columns": [ { "key": "gauge", "label": "Gauge", "type": "text", "required": true }, { "key": "due", "label": "Calibration due", "type": "date" } ] }
          ],
          "totals": [ { "column": "dev_abs", "function": "max" }, { "column": "measured", "function": "average" }, { "column": "balloon", "function": "count" } ],
          "runningTotals": [ "dev_abs" ]
        },
        {
          "key": "checks", "title": "Visual checklist", "section": "results", "fixedRows": ["Burrs", "Scratches", "Marking"],
          "columns": [ { "key": "ok", "label": "Result", "type": "passfail", "required": true }, { "key": "note", "label": "Note", "type": "text" } ]
        }
      ],
      "signatures": [
        { "key": "inspector", "label": "Inspected by", "role": "performed", "meaning": "I performed this inspection", "section": "results" },
        { "key": "reviewer", "label": "Reviewed by", "role": "reviewed", "meaning": "I reviewed the results", "distinctFrom": ["inspector"] }
      ]
    }
    """;

    static Dictionary<string, string?> Input(bool fail = false) => new()
    {
        ["lot"] = "L-100", ["qty"] = "4", ["price"] = "12.5", ["outcome"] = fail ? "Fail" : "Pass", ["reason"] = fail ? "Burr on edge" : "ignored when hidden",
        ["ops"] = """[{"op_no":"10","op_minutes":"5"},{"op_no":"20","op_minutes":"7.5"}]""",
        ["dims"] = """[{"balloon":"1","zone":"A","nominal":"10","lsl":"9.9","usl":"10.1","measured":"10.05","gauges":"[{\"gauge\":\"CAL-1\",\"due\":\"2027-01-01\"}]"},{"balloon":"2","zone":"A","nominal":"5","lsl":"4.9","usl":"5.1","measured":"5.2"},{"balloon":"3","zone":"B","nominal":"2","lsl":"1.95","usl":"2.05","measured":"2"}]""",
        ["checks"] = """[{"ok":"pass"},{"ok":"fail","note":"light"},{"ok":"na"}]""",
    };

    [Fact]
    public void Full_definition_calculates_tables_sections_and_totals()
    {
        var def = FormDefinitions.Parse(Inspection);
        var o = FormDefinitions.Apply(def, Input(), requireComplete: true);
        Assert.Equal("50.00", o["value"]);
        Assert.Null(o["reason"]); // conditional section hidden -> cleared and not required
        Assert.Equal("12.5", o["total_minutes"]);
        var ops = JsonNode.Parse(o["ops"]!)!.AsArray();
        Assert.Equal("15", ops[1]!["op_cost"]!.GetValue<string>());
        var dims = JsonNode.Parse(o["dims"]!)!;
        Assert.Equal("PASS", dims["rows"]![0]!["result"]!.GetValue<string>());
        Assert.Equal("FAIL", dims["rows"]![1]!["result"]!.GetValue<string>());
        Assert.Equal("0.25", dims["rows"]![2]!["dev_abs_running"]!.GetValue<string>());
        Assert.Equal("0.200", dims["totals"]!["max_dev_abs"]!.GetValue<string>());
        Assert.Equal("3", dims["totals"]!["count_balloon"]!.GetValue<string>());
        var groups = dims["subtotals"]!.AsArray();
        Assert.Equal(2, groups.Count);
        Assert.Equal("0.200", groups[0]!["max_dev_abs"]!.GetValue<string>());
        Assert.Equal("CAL-1", JsonNode.Parse(dims["rows"]![0]!["gauges"]!.GetValue<string>())!["rows"]![0]!["gauge"]!.GetValue<string>());
        Assert.Equal("0.200", o["worst"]);
        Assert.Equal("1", o["fails"]);
        var checks = JsonNode.Parse(o["checks"]!)!["rows"]!.AsArray();
        Assert.Equal(3, checks.Count);
    }

    [Fact]
    public void Conditional_section_fields_are_required_when_shown()
    {
        var def = FormDefinitions.Parse(Inspection);
        var input = Input(fail: true); input["reason"] = null;
        var e = Assert.Throws<AppException>(() => FormDefinitions.Apply(def, input, requireComplete: true));
        Assert.Contains("Reason is required", e.Message);
        Assert.Equal("Burr on edge", FormDefinitions.Apply(def, Input(fail: true), true)["reason"]);
    }

    [Fact]
    public void Row_and_instance_validation_names_the_place()
    {
        var def = FormDefinitions.Parse(Inspection);
        var input = Input();
        input["ops"] = """[{"op_minutes":"5"}]""";
        input["checks"] = """[{"ok":"pass"},{},{"ok":"maybe"}]""";
        input["qty"] = "2.5";
        var e = Assert.Throws<AppException>(() => FormDefinitions.Apply(def, input, requireComplete: true));
        Assert.Contains("Operations 1: Operation number is required", e.Message);
        Assert.Contains("Visual checklist Scratches: Result is required", e.Message);
        Assert.Contains("Visual checklist Marking: Result must be pass, fail or N/A", e.Message);
        Assert.Contains("Quantity must be a whole number", e.Message);
        // drafts can be incomplete but still must be well-formed
        input["checks"] = """[{"ok":"pass"}]"""; input["qty"] = "3"; input["ops"] = "[]";
        var draft = FormDefinitions.Apply(def, input, requireComplete: false);
        Assert.Equal(3, JsonNode.Parse(draft["checks"]!)!["rows"]!.AsArray().Count);
    }

    [Fact]
    public void Repeat_and_row_limits_are_enforced()
    {
        var def = FormDefinitions.Parse(Inspection);
        var input = Input();
        input["ops"] = "[" + string.Join(",", Enumerable.Range(1, 6).Select(i => $"{{\"op_no\":\"{i}\"}}")) + "]";
        Assert.Contains("at most 5 times", Assert.Throws<AppException>(() => FormDefinitions.Apply(def, input, true)).Message);
        input = Input(); input["dims"] = "[]";
        Assert.Contains("at least 1 rows", Assert.Throws<AppException>(() => FormDefinitions.Apply(def, input, true)).Message);
        input = Input(); input["nope"] = "x";
        Assert.Contains("Unknown field 'nope'", Assert.Throws<AppException>(() => FormDefinitions.Apply(def, input, false)).Message);
    }

    [Theory]
    [InlineData("\"fields\":[{\"key\":\"a\",\"label\":\"A\",\"type\":\"calc\",\"formula\":\"b + 1\"}]", "not a field")]
    [InlineData("\"fields\":[{\"key\":\"a\",\"label\":\"A\",\"type\":\"widget\"}]", "unknown type")]
    [InlineData("\"fields\":[{\"key\":\"a\",\"label\":\"A\",\"type\":\"text\",\"section\":\"zz\"}]", "unknown section")]
    [InlineData("\"sections\":[{\"key\":\"s\",\"title\":\"S\",\"parent\":\"s\"}],\"fields\":[{\"key\":\"a\",\"label\":\"A\",\"type\":\"text\"}]", "unknown parent")]
    [InlineData("\"tables\":[{\"key\":\"t\",\"title\":\"T\",\"columns\":[{\"key\":\"c\",\"label\":\"C\",\"type\":\"text\"}],\"totals\":[{\"column\":\"c\",\"function\":\"sum\"}]}]", "must be numeric")]
    [InlineData("\"tables\":[{\"key\":\"t\",\"title\":\"T\",\"columns\":[{\"key\":\"c\",\"label\":\"C\",\"type\":\"text\"}],\"headerGroups\":[{\"label\":\"G\",\"span\":2}]}]", "span exactly")]
    [InlineData("\"fields\":[{\"key\":\"a\",\"label\":\"A\",\"type\":\"text\"}],\"signatures\":[{\"key\":\"s\",\"label\":\"S\",\"role\":\"boss\",\"meaning\":\"m\"}]", "role must be")]
    [InlineData("\"fields\":[{\"key\":\"a\",\"label\":\"A\",\"type\":\"text\"}],\"signatures\":[{\"key\":\"a\",\"label\":\"S\",\"role\":\"approved\",\"meaning\":\"m\"}]", "used twice")]
    [InlineData("\"page\":{\"orientation\":\"sideways\"},\"fields\":[{\"key\":\"a\",\"label\":\"A\",\"type\":\"text\"}]", "orientation")]
    [InlineData("\"fields\":[{\"key\":\"m\",\"label\":\"M\",\"type\":\"measurement\"}]", "nominal value or limits")]
    [InlineData("\"fields\":[{\"key\":\"x\",\"label\":\"X\",\"type\":\"calc\",\"formula\":\"y\"},{\"key\":\"y\",\"label\":\"Y\",\"type\":\"calc\",\"formula\":\"x\"}]", "loop")]
    public void Invalid_definitions_are_rejected(string body, string message)
    {
        var json = "{\"schemaVersion\":2," + body + "}";
        var e = Assert.Throws<AppException>(() => { var d = FormDefinitions.Parse(json); FormDefinitions.Apply(d, new Dictionary<string, string?>(), false); });
        Assert.Contains(message, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Version_one_lists_still_work_and_round_trip()
    {
        const string v1 = """[{"key":"d1","label":"D1","type":"number"},{"key":"d2","label":"D2","type":"number"},{"key":"avg","label":"Average","type":"calc","formula":"(d1 + d2) / 2"}]""";
        var def = FormDefinitions.Parse(v1);
        Assert.Equal(1, def.SchemaVersion);
        Assert.StartsWith("[", FormDefinitions.Serialize(def));
        Assert.Equal("1.5", FormDefinitions.Apply(def, new Dictionary<string, string?> { ["d1"] = "1", ["d2"] = "2" }, true)["avg"]);
        Assert.Null(FormSchema.Apply(FormSchema.Parse(v1), new() { ["d1"] = "1" }, false)["avg"]);
    }

    [Fact]
    public void Typed_field_values_are_normalized()
    {
        var def = FormDefinitions.Parse("""
        {"schemaVersion":2,"fields":[
          {"key":"t","label":"Time","type":"time"},{"key":"dt","label":"When","type":"datetime"},
          {"key":"mc","label":"Pick","type":"multichoice","options":["a","b","c"]},{"key":"em","label":"Email","type":"email"},
          {"key":"u","label":"Owner","type":"user"},{"key":"f","label":"Files","type":"attachment"},{"key":"w","label":"Web","type":"url"},
          {"key":"i","label":"Read this first","type":"instruction"}]}
        """);
        var id = Guid.NewGuid();
        var o = FormDefinitions.Apply(def, new Dictionary<string, string?>
        {
            ["t"] = "07:30", ["dt"] = "2026-10-17T09:15:00-04:00", ["mc"] = """["c","a","a"]""", ["em"] = "qa@example.com",
            ["u"] = id.ToString().ToUpperInvariant(), ["f"] = id.ToString(), ["w"] = "https://example.com/spec",
        }, true);
        Assert.Equal("2026-10-17T13:15:00Z", o["dt"]);
        Assert.Equal("""["a","c"]""", o["mc"]);
        Assert.Equal(id.ToString(), o["u"]);
        Assert.Equal($"[\"{id}\"]", o["f"]);
        var e = Assert.Throws<AppException>(() => FormDefinitions.Apply(def, new Dictionary<string, string?> { ["t"] = "25:00", ["mc"] = """["z"]""", ["em"] = "nope", ["u"] = "x", ["w"] = "javascript:alert(1)" }, false));
        foreach (var m in new[] { "Time must be a time", "Pick must be a list", "Email must be an email", "Owner must reference", "Web must be a web address" }) Assert.Contains(m, e.Message);
    }
}
