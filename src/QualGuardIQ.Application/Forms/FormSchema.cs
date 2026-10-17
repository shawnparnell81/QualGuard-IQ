using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using QualGuardIQ.Application.Abstractions;

namespace QualGuardIQ.Application.Forms;

public sealed class FormField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>text, longtext, number, date, boolean, choice, calc</summary>
    public string Type { get; set; } = "text";
    public bool Required { get; set; }
    public List<string>? Options { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public string? Unit { get; set; }
    /// <summary>calc fields: arithmetic over number field keys, e.g. "(d1 + d2) / 2".</summary>
    public string? Formula { get; set; }
    public string? Section { get; set; }
}

/// <summary>Parses, validates and evaluates controlled form schemas. Calculated values are always computed on the server.</summary>
public static class FormSchema
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private static readonly HashSet<string> Types = ["text", "longtext", "number", "date", "boolean", "choice", "calc"];

    public static List<FormField> Parse(string json)
    {
        List<FormField>? fields;
        try { fields = JsonSerializer.Deserialize<List<FormField>>(json, Json); }
        catch (JsonException) { throw new AppException("The form definition is not valid JSON."); }
        if (fields is null || fields.Count == 0) throw new AppException("A form needs at least one field.");
        if (fields.Count > 300) throw new AppException("A form can have at most 300 fields.");
        var keys = new HashSet<string>();
        foreach (var f in fields)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(f.Key ?? "", "^[a-z][a-z0-9_]{0,49}$")) throw new AppException($"Field key '{f.Key}' must be lowercase letters, numbers and underscores.");
            if (!keys.Add(f.Key!)) throw new AppException($"Field key '{f.Key}' is used twice.");
            if (string.IsNullOrWhiteSpace(f.Label)) throw new AppException($"Field '{f.Key}' needs a label.");
            if (!Types.Contains(f.Type)) throw new AppException($"Field '{f.Key}' has unknown type '{f.Type}'.");
            if (f.Type == "choice" && (f.Options is null || f.Options.Count == 0)) throw new AppException($"Choice field '{f.Key}' needs options.");
            if (f.Type == "calc" && string.IsNullOrWhiteSpace(f.Formula)) throw new AppException($"Calculated field '{f.Key}' needs a formula.");
        }
        foreach (var f in fields.Where(f => f.Type == "calc"))
            foreach (var id in Calc.Identifiers(f.Formula!))
                if (!fields.Any(o => o.Key == id && o.Type is "number" or "calc" && o.Key != f.Key))
                    throw new AppException($"Formula of '{f.Key}' uses '{id}', which is not a number field.");
        return fields;
    }

    public static string Serialize(List<FormField> fields) => JsonSerializer.Serialize(fields, Json);

    /// <summary>Validates entered values, computes calculated fields, and returns the normalized values.</summary>
    public static Dictionary<string, string?> Apply(List<FormField> fields, Dictionary<string, string?> input, bool requireComplete)
    {
        var errors = new List<string>();
        foreach (var k in input.Keys.Where(k => fields.All(f => f.Key != k))) errors.Add($"Unknown field '{k}'.");
        var output = new Dictionary<string, string?>();
        var numbers = new Dictionary<string, decimal>();
        foreach (var f in fields.Where(f => f.Type != "calc"))
        {
            input.TryGetValue(f.Key, out var v);
            v = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            if (v is null) { if (requireComplete && f.Required) errors.Add($"{f.Label} is required."); output[f.Key] = null; continue; }
            switch (f.Type)
            {
                case "number":
                    if (!decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var n)) { errors.Add($"{f.Label} must be a number."); break; }
                    if (f.Min is { } min && n < min) errors.Add($"{f.Label} must be at least {min}.");
                    if (f.Max is { } max && n > max) errors.Add($"{f.Label} must be at most {max}.");
                    numbers[f.Key] = n;
                    break;
                case "date":
                    if (!DateOnly.TryParseExact(v, "yyyy-MM-dd", out _)) errors.Add($"{f.Label} must be a date.");
                    break;
                case "boolean":
                    if (v is not ("true" or "false")) errors.Add($"{f.Label} must be yes or no.");
                    break;
                case "choice":
                    if (!f.Options!.Contains(v)) errors.Add($"{f.Label} must be one of: {string.Join(", ", f.Options!)}.");
                    break;
                default:
                    if (v.Length > (f.Type == "longtext" ? 10000 : 1000)) errors.Add($"{f.Label} is too long.");
                    break;
            }
            output[f.Key] = v;
        }
        var pending = fields.Where(f => f.Type == "calc").ToList();
        for (var pass = 0; pass < pending.Count + 1 && pending.Count > 0; pass++)
        {
            foreach (var f in pending.ToList())
            {
                var ids = Calc.Identifiers(f.Formula!);
                if (ids.Any(i => fields.First(x => x.Key == i).Type == "calc" && !numbers.ContainsKey(i) && pending.Any(p => p.Key == i))) continue;
                pending.Remove(f);
                if (ids.All(numbers.ContainsKey))
                {
                    try
                    {
                        var r = Math.Round(Calc.Evaluate(f.Formula!, numbers), 6);
                        numbers[f.Key] = r;
                        output[f.Key] = Math.Round(r, 6).ToString("0.######", CultureInfo.InvariantCulture);
                    }
                    catch (DivideByZeroException) { output[f.Key] = null; }
                }
                else output[f.Key] = null;
            }
        }
        if (pending.Count > 0) errors.Add("Calculated fields refer to each other in a loop.");
        if (errors.Count > 0) throw new AppException(string.Join(" ", errors));
        return output;
    }
}

/// <summary>Small arithmetic evaluator: numbers, identifiers, + - * / and parentheses. No functions or code execution.</summary>
public static class Calc
{
    public static IReadOnlySet<string> Identifiers(string formula)
    {
        var set = new HashSet<string>();
        foreach (var t in Tokenize(formula)) if (char.IsLetter(t[0])) set.Add(t);
        return set;
    }

    public static decimal Evaluate(string formula, IReadOnlyDictionary<string, decimal> vars)
    {
        var tokens = Tokenize(formula);
        var pos = 0;
        var result = Expr();
        if (pos != tokens.Count) throw new AppException($"Formula '{formula}' is not valid.");
        return result;

        decimal Expr()
        {
            var v = Term();
            while (pos < tokens.Count && tokens[pos] is "+" or "-") { var op = tokens[pos++]; var r = Term(); v = op == "+" ? v + r : v - r; }
            return v;
        }
        decimal Term()
        {
            var v = Factor();
            while (pos < tokens.Count && tokens[pos] is "*" or "/") { var op = tokens[pos++]; var r = Factor(); v = op == "*" ? v * r : v / r; }
            return v;
        }
        decimal Factor()
        {
            if (pos >= tokens.Count) throw new AppException($"Formula '{formula}' is not valid.");
            var t = tokens[pos++];
            if (t == "-") return -Factor();
            if (t == "(") { var v = Expr(); if (pos >= tokens.Count || tokens[pos++] != ")") throw new AppException($"Formula '{formula}' has unbalanced parentheses."); return v; }
            if (char.IsDigit(t[0]) || t[0] == '.') return decimal.Parse(t, CultureInfo.InvariantCulture);
            if (char.IsLetter(t[0])) return vars.TryGetValue(t, out var x) ? x : throw new AppException($"Unknown value '{t}'.");
            throw new AppException($"Formula '{formula}' is not valid.");
        }
    }

    private static List<string> Tokenize(string s)
    {
        var list = new List<string>();
        for (var i = 0; i < s.Length;)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if ("+-*/()".Contains(c)) { list.Add(c.ToString()); i++; continue; }
            var start = i;
            if (char.IsDigit(c) || c == '.') { while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++; }
            else if (char.IsLetter(c)) { while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++; }
            else throw new AppException($"Formula contains an invalid character '{c}'.");
            list.Add(s[start..i]);
        }
        return list;
    }
}
