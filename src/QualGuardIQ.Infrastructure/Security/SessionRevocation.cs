using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Security;

/// <summary>
/// Server-side sign-out. Signing out records the session id, so a copied cookie or access token stops working at once, and every
/// session ends a fixed time after sign-in however active it is. Other sessions of the same person keep working.
/// </summary>
public sealed class SessionRevocation(AppDbContext db, IOptions<AuthOptions> options, IClock clock)
{
    public TimeSpan AbsoluteLifetime => TimeSpan.FromHours(Math.Clamp(options.Value.SessionAbsoluteHours, 1, 24 * 30));

    /// <summary>Null when the session may continue; otherwise the reason it must end.</summary>
    public async Task<string?> RejectReasonAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var sid = principal.FindFirstValue(SessionClaims.SessionId);
        var iat = principal.FindFirstValue(SessionClaims.IssuedAt);
        if (sid is null || !long.TryParse(iat, NumberStyles.Integer, CultureInfo.InvariantCulture, out var issued)) return "Session from an older version";
        if (clock.UtcNow - DateTimeOffset.FromUnixTimeSeconds(issued) > AbsoluteLifetime) return "Session reached its maximum length";
        if (await db.RevokedSessions.AsNoTracking().AnyAsync(r => r.SessionId == sid, ct)) return "Session was signed out";
        return null;
    }

    /// <summary>Ends the session the principal belongs to. Also clears out expired entries.</summary>
    public async Task RevokeAsync(ClaimsPrincipal principal, Guid userId, CancellationToken ct = default)
    {
        var sid = principal.FindFirstValue(SessionClaims.SessionId);
        if (sid is null) return;
        var now = clock.UtcNow;
        var issued = long.TryParse(principal.FindFirstValue(SessionClaims.IssuedAt), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? DateTimeOffset.FromUnixTimeSeconds(s) : now;
        await db.RevokedSessions.Where(r => r.ExpiresAt < now).ExecuteDeleteAsync(ct);
        if (!await db.RevokedSessions.AnyAsync(r => r.SessionId == sid, ct))
        {
            db.RevokedSessions.Add(new RevokedSession { SessionId = sid, UserId = userId, RevokedAt = now, ExpiresAt = issued + AbsoluteLifetime + TimeSpan.FromHours(1) });
            await db.SaveChangesAsync(ct);
        }
    }
}
