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
    /// <summary>One of <see cref="FormDefinitions.FieldTypes"/> (table columns may also be "table").</summary>
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
    /// <summary>Help text shown under the input and in the print layout's instructions.</summary>
    public string? Help { get; set; }
    /// <summary>
    /// When the record is filled from another record (a PPAP common data page, a characteristics list, a CAPA), the key of the value to copy
    /// into this field or column. Null copies the value with the same key.
    /// </summary>
    public string? Prefill { get; set; }
    public string? Placeholder { get; set; }
    /// <summary>Layout width in a 12-column grid (print and screen).</summary>
    public int? Width { get; set; }
    /// <summary>Decimals shown and stored for numeric and calculated values.</summary>
    public int? Decimals { get; set; }
    public int? MaxLength { get; set; }
    /// <summary>Measurement fields: nominal value; Min/Max are the tolerance limits (out-of-tolerance values are recorded, not rejected).</summary>
    public decimal? Nominal { get; set; }
    /// <summary>Calculated true/false results: the two words shown, e.g. ["PASS", "FAIL"].</summary>
    public List<string>? ResultLabels { get; set; }
    /// <summary>Attachment and photo fields: accepted file extensions, e.g. [".pdf", ".xlsx"].</summary>
    public List<string>? Accept { get; set; }
    /// <summary>Nested table columns (table columns of type "table").</summary>
    public List<FormField>? Columns { get; set; }
    /// <summary>Where an imported field came from (sheet!cell, page/region, content control tag).</summary>
    public string? SourceLocation { get; set; }
    /// <summary>Calculated fields converted from a spreadsheet: the original formula, kept for traceability.</summary>
    public string? SourceFormula { get; set; }
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

    /// <summary>Parses a version 1 field list (also used by library components). Full form definitions use <see cref="FormDefinitions"/>.</summary>
    public static List<FormField> Parse(string json)
    {
        List<FormField>? fields;
        try { fields = JsonSerializer.Deserialize<List<FormField>>(json, Json); }
        catch (JsonException) { throw new AppException("The form definition is not valid JSON."); }
        if (fields is null || fields.Count == 0) throw new AppException("A form needs at least one field.");
        if (fields.Count > 300) throw new AppException("A form can have at most 300 fields.");
        FormDefinitions.Validate(new FormDefinition { SchemaVersion = 1, Fields = fields });
        return fields;
    }

    public static string Serialize(List<FormField> fields) => JsonSerializer.Serialize(fields, Json);

    /// <summary>Validates entered values, computes calculated fields, and returns the normalized values.</summary>
    public static Dictionary<string, string?> Apply(List<FormField> fields, Dictionary<string, string?> input, bool requireComplete)
        => FormDefinitions.Apply(new FormDefinition { SchemaVersion = 1, Fields = fields }, input, requireComplete);

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
