using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Rules;

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
    /// <summary>Dynamic rule: the field is shown (and validated) only when this condition on another field holds. Hidden fields are cleared.</summary>
    public FieldCondition? VisibleWhen { get; set; }
    /// <summary>Dynamic rule: the field becomes required when this condition holds.</summary>
    public FieldCondition? RequiredWhen { get; set; }
    /// <summary>Dynamic validation: text must match this regular expression.</summary>
    public string? Pattern { get; set; }
    public string? PatternMessage { get; set; }
    /// <summary>Dynamic validation: comparisons against another field or a fixed value (for example end date ≥ start date).</summary>
    public List<FieldCheck>? Checks { get; set; }
    /// <summary>Library component (key@version) this field was inserted from.</summary>
    public string? FromComponent { get; set; }
}

public sealed class FieldCondition
{
    public string Field { get; set; } = "";
    public string Operator { get; set; } = "equals";
    public string? Value { get; set; }
}

public sealed class FieldCheck
{
    public string Operator { get; set; } = "greaterOrEqual";
    /// <summary>Another field key to compare with; when empty, Value is used.</summary>
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string Message { get; set; } = "";
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
        foreach (var f in fields)
        {
            foreach (var (c, what) in new[] { (f.VisibleWhen, "visibility rule"), (f.RequiredWhen, "required rule") })
            {
                if (c is null) continue;
                if (c.Field == f.Key || !keys.Contains(c.Field)) throw new AppException($"The {what} of '{f.Key}' refers to '{c.Field}', which is not another field of this form.");
                if (!Conditions.Operators.Contains(c.Operator)) throw new AppException($"The {what} of '{f.Key}' has unknown operator '{c.Operator}'.");
            }
            if (f.Pattern is { Length: > 0 })
            {
                if (f.Pattern.Length > 300) throw new AppException($"The pattern of '{f.Key}' is too long.");
                try { _ = new System.Text.RegularExpressions.Regex(f.Pattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { throw new AppException($"The pattern of '{f.Key}' is not a valid regular expression."); }
            }
            foreach (var k in f.Checks ?? [])
            {
                if (!Conditions.Operators.Contains(k.Operator) || k.Operator is "isEmpty" or "isNotEmpty") throw new AppException($"A check on '{f.Key}' has an unsupported operator '{k.Operator}'.");
                if (string.IsNullOrWhiteSpace(k.Message)) throw new AppException($"A check on '{f.Key}' needs a message.");
                if (!string.IsNullOrWhiteSpace(k.Field) && !keys.Contains(k.Field)) throw new AppException($"A check on '{f.Key}' compares with unknown field '{k.Field}'.");
                if (string.IsNullOrWhiteSpace(k.Field) && string.IsNullOrWhiteSpace(k.Value)) throw new AppException($"A check on '{f.Key}' needs a field or a value to compare with.");
            }
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
        var visible = VisibleKeys(fields, input);
        var raw = input.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);
        foreach (var f in fields.Where(f => f.Type != "calc"))
        {
            input.TryGetValue(f.Key, out var v);
            v = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            if (!visible.Contains(f.Key)) { output[f.Key] = null; continue; } // hidden by a dynamic rule: cleared, never required
            var required = f.Required || (f.RequiredWhen is { } rw && Conditions.Matches(new Condition(rw.Field, rw.Operator, rw.Value), raw));
            if (v is null) { if (requireComplete && required) errors.Add($"{f.Label} is required."); output[f.Key] = null; continue; }
            if (f.Pattern is { Length: > 0 } pat && f.Type is "text" or "longtext" && !System.Text.RegularExpressions.Regex.IsMatch(v, pat, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)))
                errors.Add(string.IsNullOrWhiteSpace(f.PatternMessage) ? $"{f.Label} is not in the expected format." : f.PatternMessage);
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
        var values = output.ToDictionary(kv => kv.Key, kv => (object?)(numbers.TryGetValue(kv.Key, out var n) ? n : kv.Value), StringComparer.Ordinal);
        foreach (var f in fields.Where(f => f.Checks is { Count: > 0 } && output.GetValueOrDefault(f.Key) is not null))
            foreach (var k in f.Checks!)
            {
                var other = string.IsNullOrWhiteSpace(k.Field) ? k.Value : output.GetValueOrDefault(k.Field!);
                if (other is null) continue; // nothing to compare with yet
                if (!Conditions.Matches(new Condition(f.Key, k.Operator, other), values)) errors.Add(k.Message);
            }
        if (errors.Count > 0) throw new AppException(string.Join(" ", errors));
        return output;
    }

    /// <summary>Keys of fields shown for the given values (dynamic visibility rules; a field hidden by a hidden field is hidden too).</summary>
    public static HashSet<string> VisibleKeys(List<FormField> fields, IReadOnlyDictionary<string, string?> input)
    {
        var raw = input.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);
        var visible = fields.Select(f => f.Key).ToHashSet();
        for (var pass = 0; pass < fields.Count; pass++)
        {
            var changed = false;
            foreach (var f in fields.Where(f => f.VisibleWhen is not null && visible.Contains(f.Key)))
            {
                var c = f.VisibleWhen!;
                if (!visible.Contains(c.Field) || !Conditions.Matches(new Condition(c.Field, c.Operator, c.Value), raw)) { visible.Remove(f.Key); changed = true; }
            }
            if (!changed) break;
        }
        return visible;
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
