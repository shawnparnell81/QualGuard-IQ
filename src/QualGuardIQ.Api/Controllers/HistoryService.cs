using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

public sealed record HistoryItem(DateTimeOffset OccurredAt, string Event, string? Step, string? UserName, string? Comment);

/// <summary>Workflow history of any record, read from the shared approval engine and the audit trail (no separate store).</summary>
public static class HistoryService
{
    public static async Task<List<HistoryItem>> WorkflowAsync(AppDbContext db, string recordType, Guid id, string[] entityTypes, CancellationToken ct)
    {
        var reqs = await db.ApprovalRequests.AsNoTracking().Include(r => r.Decisions).Where(r => r.RecordType == recordType && r.RecordId == id).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().Where(u => reqs.Select(r => r.SubmittedById).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var items = new List<HistoryItem>();
        foreach (var r in reqs)
        {
            items.Add(new(r.CreatedAt, "Submitted for approval", null, users.GetValueOrDefault(r.SubmittedById), null));
            items.AddRange(r.Decisions.Select(d => new HistoryItem(d.SignedAt, d.Approved ? "Signed: " + d.Meaning : "Not approved", d.StepName, d.SignerName, d.Comment)));
            if (r.Status == Domain.Shared.ApprovalStatus.Cancelled && r.CompletedAt is { } c) items.Add(new(c, "Approval withdrawn", null, null, null));
        }
        var audits = await db.AuditEntries.AsNoTracking().Where(e => e.EntityId == id && entityTypes.Contains(e.EntityType) && (e.Action == "Created" || e.Changes != null)).OrderBy(e => e.OccurredAt).ToListAsync(ct);
        foreach (var a in audits)
        {
            if (a.Action == "Created") { items.Add(new(a.OccurredAt, "Created", null, a.UserName, null)); continue; }
            using var doc = JsonDocument.Parse(a.Changes!);
            foreach (var key in new[] { "Status", "OverallResult" })
                if (doc.RootElement.TryGetProperty(key, out var ch))
                    items.Add(new(a.OccurredAt, $"{key}: {Val(ch, "from")} → {Val(ch, "to")}", null, a.UserName, null));
        }
        return items.OrderBy(i => i.OccurredAt).ToList();
    }

    private static string Val(JsonElement ch, string name)
    {
        // readable enum name when the audit entry has one (entries written before names were stored show the number)
        if (ch.ValueKind == JsonValueKind.Object && ch.TryGetProperty(name + "Name", out var n) && n.ValueKind == JsonValueKind.String)
            return QualGuardIQ.Infrastructure.Persistence.AuditText.Words(n.GetString()!);
        if (ch.ValueKind == JsonValueKind.Object && ch.TryGetProperty(name, out var v)) return v.ToString();
        if (ch.ValueKind == JsonValueKind.Array && ch.GetArrayLength() == 2) return ch[name == "from" ? 0 : 1].ToString();
        return name == "to" ? ch.ToString() : "";
    }
}
