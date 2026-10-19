using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Services;

/// <summary>Writes in-app notifications. Email copies are delivered by the worker's recurring job.</summary>
public sealed class Notifier(AppDbContext db) : INotifier
{
    public async Task NotifyAsync(IEnumerable<Guid> userIds, string title, string? body, RecordInfo? record, bool email = true, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = userIds.Distinct().ToList();
        // e-mail copies follow the organization switch and each person's own setting; the in-app notification is always written
        var noEmail = new HashSet<Guid>();
        if (email && ids.Count > 0)
        {
            if (!await db.OrganizationSettings.AsNoTracking().Select(s => s.EmailNotificationsEnabled).FirstOrDefaultAsync(ct)) email = false;
            else noEmail = (await db.UserPreferences.AsNoTracking().Where(p => ids.Contains(p.UserId) && !p.EmailNotifications).Select(p => p.UserId).ToListAsync(ct)).ToHashSet();
        }
        foreach (var id in ids)
            db.Notifications.Add(new Notification
            {
                UserId = id, CreatedAt = now, Title = title, Body = body, RecordType = record?.Type, RecordId = record?.Id,
                Link = record?.Url, EmailRequested = email && !noEmail.Contains(id)
            });
        await db.SaveChangesAsync(ct);
    }
}
