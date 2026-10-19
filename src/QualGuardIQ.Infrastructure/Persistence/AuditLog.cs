using System.Text.Json;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Audit;

namespace QualGuardIQ.Infrastructure.Persistence;

/// <summary>
/// Writes an audit entry for a change the automatic audit can't see (link tables, bulk deletes, whole-list replacements).
/// The entry is saved with the change it describes, in the same SaveChanges.
/// </summary>
public static class AuditLog
{
    public static AuditEntry Add(AppDbContext db, ICurrentUser user, DateTimeOffset now, string action, string entityType, Guid entityId, Guid? siteId,
        string description, object? changes = null)
    {
        var e = new AuditEntry
        {
            OccurredAt = now, UserId = user.UserId, UserName = user.UserName, Action = action, EntityType = entityType, EntityId = entityId,
            SiteId = siteId, Description = AuditText.Fit(description), Changes = changes is null ? null : JsonSerializer.Serialize(changes),
            CorrelationId = user.CorrelationId,
        };
        db.AuditEntries.Add(e);
        return e;
    }
}
