using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>One no-code condition: a record field, an operator and a value (for example Severity equals Major).</summary>
public sealed record Condition(string Field, string Operator, string? Value);

/// <summary>
/// Shared condition evaluation used by the workflow designer (conditional and decision-tree steps) and the business rule engine.
/// Fields are read from the record itself, so a condition can use any stored field of the record type without code.
/// </summary>
public sealed class RecordConditions(AppDbContext db, IRecordRegistry records)
{
    public static readonly string[] Operators = ["equals", "notEquals", "greaterThan", "greaterOrEqual", "lessThan", "lessOrEqual", "contains", "in", "isEmpty", "isNotEmpty"];

    /// <summary>Field names a condition can use for a record type (scalar properties of the stored record).</summary>
    public IReadOnlyList<(string Name, string Type, string[]? Options)> FieldsOf(string recordType)
    {
        var et = EntityFor(recordType);
        var list = new List<(string, string, string[]?)> { ("Number", "text", null), ("Title", "text", null), ("Status", "text", null) };
        if (et is null) return list;
        foreach (var p in et.GetProperties().Where(p => p.PropertyInfo is not null && !p.IsShadowProperty()))
        {
            var t = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
            if (list.Any(x => x.Item1 == p.Name) || p.Name is "Id" or "RowVersion" || p.Name.StartsWith("Encrypted")) continue;
            string? kind = t.IsEnum ? "choice" : t == typeof(string) ? "text" : t == typeof(bool) ? "yesno" :
                t == typeof(decimal) || t == typeof(int) || t == typeof(long) || t == typeof(double) ? "number" :
                t == typeof(DateOnly) || t == typeof(DateTimeOffset) || t == typeof(DateTime) ? "date" : t == typeof(Guid) ? "id" : null;
            if (kind is null) continue;
            list.Add((p.Name, kind, t.IsEnum ? Enum.GetNames(t) : null));
        }
        return list;
    }

    Microsoft.EntityFrameworkCore.Metadata.IEntityType? EntityFor(string recordType) =>
        db.Model.GetEntityTypes().FirstOrDefault(e => !e.IsOwned() && string.Equals(e.ClrType.Name, recordType, StringComparison.OrdinalIgnoreCase));

    /// <summary>Current field values of a record (no permission check: callers have already loaded the record through the registry).</summary>
    public async Task<Dictionary<string, object?>> ValuesAsync(string recordType, Guid id, CancellationToken ct)
    {
        var info = await records.GetAsync(recordType, id, ct);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Number"] = info.Number, ["Title"] = info.Title, ["Status"] = info.Status, ["SiteId"] = info.SiteId, ["DepartmentId"] = info.DepartmentId };
        if (EntityFor(recordType) is { } et && await db.FindAsync(et.ClrType, [id], ct) is { } entity)
        {
            foreach (var p in et.GetProperties().Where(p => p.PropertyInfo is not null))
                if (!p.Name.StartsWith("Encrypted")) values[p.Name] = p.PropertyInfo!.GetValue(entity);
        }
        return values;
    }

    public static bool AllMatch(IEnumerable<Condition> conditions, IReadOnlyDictionary<string, object?> values) => conditions.All(c => Matches(c, values));

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

    /// <summary>Checks a condition is well formed for the record type; throws a readable error otherwise.</summary>
    public void Validate(string recordType, Condition c)
    {
        if (!Operators.Contains(c.Operator)) throw new AppException($"Unknown operator '{c.Operator}'.");
        if (FieldsOf(recordType).All(f => !string.Equals(f.Name, c.Field, StringComparison.OrdinalIgnoreCase))) throw new AppException($"'{c.Field}' is not a field of {recordType}.");
        if (c.Operator is not ("isEmpty" or "isNotEmpty") && string.IsNullOrWhiteSpace(c.Value)) throw new AppException($"The condition on {c.Field} needs a value.");
    }
}
