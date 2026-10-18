using System.Reflection;
using System.Text.Json;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Infrastructure.Persistence;

/// <summary>
/// Audit for lists that are saved as a whole (process flow steps, PFMEA and control plan lines, MSA readings): compares the list
/// before and after and writes one entry that says which lines were added, removed or changed, with the old and new values.
/// </summary>
public static class LineAudit
{
    static readonly HashSet<string> Skip = [nameof(Entity.Id), nameof(Entity.RowVersion), nameof(Entity.CreatedAt), nameof(Entity.CreatedById), nameof(Entity.UpdatedAt), nameof(Entity.UpdatedById)];

    /// <summary>Field values of a line, without ids, timestamps and the parent reference.</summary>
    public static Dictionary<string, object?> Snapshot(object line, string parentProperty) =>
        line.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && !Skip.Contains(p.Name) && p.Name != parentProperty && IsScalar(p.PropertyType))
            .ToDictionary(p => p.Name, p => p.GetValue(line));

    static bool IsScalar(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(Guid) || t == typeof(DateOnly) || t == typeof(DateTimeOffset) || t == typeof(DateTime) || t == typeof(TimeOnly);
    }

    /// <summary>
    /// Adds the entry when the list changed. <paramref name="key"/> identifies a line across saves (for example its step number);
    /// <paramref name="name"/> is how a line reads in the description.
    /// </summary>
    public static bool Write<T>(AppDbContext db, ICurrentUser user, DateTimeOffset now, string entityType, Guid entityId, Guid? siteId, string what, string recordLabel,
        IEnumerable<T> before, IEnumerable<T> after, string parentProperty, Func<T, string> key, Func<T, string> name) where T : notnull
    {
        var old = Group(before, key);
        var cur = Group(after, key);
        var added = new List<(string Name, Dictionary<string, object?> Values)>();
        var removed = new List<(string Name, Dictionary<string, object?> Values)>();
        var changed = new List<(string Name, Dictionary<string, object?> From, Dictionary<string, object?> To)>();
        foreach (var (k, lines) in cur)
        {
            var prev = old.GetValueOrDefault(k) ?? [];
            for (var i = 0; i < lines.Count; i++)
            {
                var to = Snapshot(lines[i], parentProperty);
                if (i >= prev.Count) { added.Add((name(lines[i]), to)); continue; }
                var from = Snapshot(prev[i], parentProperty);
                if (JsonSerializer.Serialize(from) != JsonSerializer.Serialize(to)) changed.Add((name(lines[i]), from, to));
            }
            for (var i = lines.Count; i < prev.Count; i++) removed.Add((name(prev[i]), Snapshot(prev[i], parentProperty)));
        }
        foreach (var (k, lines) in old.Where(o => !cur.ContainsKey(o.Key)))
            removed.AddRange(lines.Select(l => (name(l), Snapshot(l, parentProperty))));
        if (added.Count + removed.Count + changed.Count == 0) return false;

        var parts = new List<string>();
        if (added.Count > 0) parts.Add("added " + string.Join(", ", added.Select(a => a.Name)));
        if (removed.Count > 0) parts.Add("removed " + string.Join(", ", removed.Select(a => a.Name)));
        foreach (var c in changed)
        {
            var diffs = c.To.Where(kv => !Equals(c.From.GetValueOrDefault(kv.Key), kv.Value))
                .Select(kv => AuditText.Change(AuditText.Words(kv.Key.EndsWith("Id", StringComparison.Ordinal) && kv.Key.Length > 2 ? kv.Key[..^2] : kv.Key), c.From.GetValueOrDefault(kv.Key), kv.Value, _ => null));
            parts.Add($"changed {c.Name} ({string.Join("; ", diffs)})");
        }
        AuditLog.Add(db, user, now, "Updated", entityType, entityId, siteId, $"Updated {what} of {recordLabel}: {string.Join("; ", parts)}", new
        {
            added = added.Select(a => a.Values),
            removed = removed.Select(r => r.Values),
            changed = changed.Select(c => new { line = c.Name, from = c.From, to = c.To }),
        });
        return true;
    }

    static Dictionary<string, List<T>> Group<T>(IEnumerable<T> lines, Func<T, string> key) =>
        lines.GroupBy(key).ToDictionary(g => g.Key, g => g.ToList());
}
