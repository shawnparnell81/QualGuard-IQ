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
        foreach (var id in userIds.Distinct())
            db.Notifications.Add(new Notification
            {
                UserId = id, CreatedAt = now, Title = title, Body = body, RecordType = record?.Type, RecordId = record?.Id,
                Link = record?.Url, EmailRequested = email
            });
        await db.SaveChangesAsync(ct);
    }
}
