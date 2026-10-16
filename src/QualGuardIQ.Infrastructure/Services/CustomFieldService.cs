using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Services;

public sealed class CustomFieldService(AppDbContext db, IRecordRegistry records)
{
    public async Task<(IReadOnlyList<CustomFieldDefinition> Fields, IReadOnlyDictionary<string, string?> Values)> GetAsync(string type, Guid id, CancellationToken ct)
    {
        var r = await records.RequireViewAsync(type, id, ct);
        var defs = await db.CustomFieldDefinitions.AsNoTracking().Where(d => d.RecordType == r.Type && !d.IsArchived).OrderBy(d => d.SortOrder).ToListAsync(ct);
        var vals = await db.CustomFieldValues.AsNoTracking().FirstOrDefaultAsync(v => v.RecordType == r.Type && v.RecordId == r.Id, ct);
        return (defs, vals?.Values ?? new Dictionary<string, string?>());
    }

    public async Task SaveAsync(string type, Guid id, Dictionary<string, string?> values, CancellationToken ct)
    {
        var r = await records.RequireEditAsync(type, id, ct);
        if (r.IsLocked) throw new AppException("This record is locked.");
        var defs = await db.CustomFieldDefinitions.AsNoTracking().Where(d => d.RecordType == r.Type && !d.IsArchived).ToListAsync(ct);
        var errors = new List<string>();
        foreach (var key in values.Keys.Where(k => defs.All(d => d.Key != k))) errors.Add($"Unknown field '{key}'.");
        foreach (var d in defs)
        {
            values.TryGetValue(d.Key, out var v);
            if (string.IsNullOrWhiteSpace(v)) { if (d.IsRequired) errors.Add($"{d.Label} is required."); continue; }
            var ok = d.FieldType switch
            {
                CustomFieldType.Number => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _),
                CustomFieldType.Date => DateOnly.TryParseExact(v, "yyyy-MM-dd", out _),
                CustomFieldType.Boolean => v is "true" or "false",
                CustomFieldType.Choice => d.Options.Contains(v),
                _ => v.Length <= 2000
            };
            if (!ok) errors.Add($"{d.Label} has an invalid value.");
        }
        if (errors.Count > 0) throw new AppException(string.Join(" ", errors));
        var row = await db.CustomFieldValues.FirstOrDefaultAsync(x => x.RecordType == r.Type && x.RecordId == r.Id, ct);
        if (row is null) { row = new CustomFieldValues { RecordType = r.Type, RecordId = r.Id }; db.CustomFieldValues.Add(row); }
        row.Values = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary();
        await db.SaveChangesAsync(ct);
    }
}
