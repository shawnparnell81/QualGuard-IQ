using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Self-service account information for the signed-in user.</summary>
[ApiController]
[Authorize]
[Route("api/account")]
public sealed class AccountController(AppDbContext db, ICurrentUser current) : ControllerBase
{
    public sealed record SignInDto(DateTimeOffset OccurredAt, string EventType, bool Succeeded, string? IpAddress, string? UserAgent, string? Detail);
    public sealed record SignInHistory(bool Enabled, IReadOnlyList<SignInDto> Items);

    private static readonly string[] SignInEvents = ["SignIn", "SignInFailed", "SignInLockedOut", "SignOut"];

    /// <summary>The user's own recent sign-in activity, shown only when the organization enables login history.</summary>
    [HttpGet("sign-ins")]
    public async Task<SignInHistory> SignIns(CancellationToken ct)
    {
        var enabled = await db.OrganizationSettings.AsNoTracking().Select(s => s.LoginHistoryEnabled).FirstOrDefaultAsync(ct);
        if (!enabled) return new SignInHistory(false, []);
        var items = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.UserId == current.UserId && SignInEvents.Contains(e.EventType))
            .OrderByDescending(e => e.OccurredAt).Take(50)
            .Select(e => new SignInDto(e.OccurredAt, e.EventType, e.Succeeded, e.IpAddress, e.UserAgent, e.Detail))
            .ToListAsync(ct);
        return new SignInHistory(true, items);
    }
}
