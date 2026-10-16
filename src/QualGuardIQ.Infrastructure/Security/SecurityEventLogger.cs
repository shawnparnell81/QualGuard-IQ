using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Security;

public interface ISecurityEventLogger
{
    Task LogAsync(string eventType, bool succeeded, Guid? userId, string? userName, string? detail = null, CancellationToken ct = default);
}

public sealed class SecurityEventLogger(AppDbContext db, ICurrentUser current, IClock clock, Microsoft.AspNetCore.Http.IHttpContextAccessor http) : ISecurityEventLogger
{
    public async Task LogAsync(string eventType, bool succeeded, Guid? userId, string? userName, string? detail = null, CancellationToken ct = default)
    {
        db.SecurityEvents.Add(new SecurityEvent
        {
            OccurredAt = clock.UtcNow,
            EventType = eventType,
            Succeeded = succeeded,
            UserId = userId,
            UserName = userName,
            IpAddress = current.IpAddress,
            UserAgent = http.HttpContext?.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 400)] : null,
            Detail = detail
        });
        await db.SaveChangesAsync(ct);
    }
}
