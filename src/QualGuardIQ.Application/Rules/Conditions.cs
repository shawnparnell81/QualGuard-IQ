using System.Globalization;

namespace QualGuardIQ.Application.Rules;

/// <summary>One no-code condition: a field, an operator and a value (for example Severity equals Major).</summary>
public sealed record Condition(string Field, string Operator, string? Value);

/// <summary>The single condition evaluator shared by the workflow designer, the form designer's dynamic rules and the business rule engine.</summary>
public static class Conditions
{
    public static readonly string[] Operators = ["equals", "notEquals", "greaterThan", "greaterOrEqual", "lessThan", "lessOrEqual", "contains", "in", "isEmpty", "isNotEmpty"];

    public static bool Matches(Condition c, IReadOnlyDictionary<string, object?> values)
    {
        values.TryGetValue(c.Field, out var raw);
        var actual = raw switch
        {
            null => null,
            Enum e => e.ToString(),
            DateTimeOffset d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString()
        };
        var expected = c.Value?.Trim() ?? "";
        bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || (raw is Enum en && b == Convert.ToInt32(en).ToString(CultureInfo.InvariantCulture));
        int? Cmp()
        {
            if (actual is null) return null;
            if (decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) && decimal.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var y)) return x.CompareTo(y);
            if (raw is Enum en && int.TryParse(expected, out var iy)) return Convert.ToInt32(en).CompareTo(iy);
            if (raw is Enum en2 && Enum.TryParse(raw.GetType(), expected, true, out var ev)) return Convert.ToInt32(en2).CompareTo(Convert.ToInt32(ev));
            return string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        return c.Operator switch
        {
            "equals" => actual is not null && Eq(actual, expected),
            "notEquals" => actual is null || !Eq(actual, expected),
            "greaterThan" => Cmp() > 0,
            "greaterOrEqual" => Cmp() >= 0,
            "lessThan" => Cmp() < 0,
            "lessOrEqual" => Cmp() <= 0,
            "contains" => actual?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true,
            "in" => actual is not null && expected.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(v => Eq(actual, v)),
            "isEmpty" => string.IsNullOrEmpty(actual),
            "isNotEmpty" => !string.IsNullOrEmpty(actual),
            _ => false
        };
    }

}
