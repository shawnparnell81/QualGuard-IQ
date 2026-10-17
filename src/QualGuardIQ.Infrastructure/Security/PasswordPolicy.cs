using System.IO.Compression;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Security;

/// <summary>The bundled breached and common password list (see Security/Data/README.md). Loaded once, checked offline.</summary>
public static class CommonPasswords
{
    static readonly Lazy<HashSet<string>> List = new(() =>
    {
        using var s = typeof(CommonPasswords).Assembly.GetManifestResourceStream("common-passwords.txt.gz") ?? throw new InvalidOperationException("common-passwords.txt.gz is missing.");
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        using var r = new StreamReader(gz);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (r.ReadLine() is { } line) if (line.Length > 0) set.Add(line);
        return set;
    });

    public static int Count => List.Value.Count;

    /// <summary>True when the password, or the password without trailing digits and symbols, is on the list (case-insensitive).</summary>
    public static bool Contains(string password)
    {
        var p = password.Trim().ToLowerInvariant();
        if (List.Value.Contains(p)) return true;
        var stem = p.TrimEnd("0123456789!@#$%^&*()-_=+.,?~ ".ToCharArray());
        return stem.Length >= 6 && stem != p && List.Value.Contains(stem);
    }
}

/// <summary>
/// Password rules from configuration, applied by Identity everywhere a password is set (new accounts, temporary passwords,
/// changes and resets): maximum length, the common list, the user's own email or name, simple runs, and the last N passwords.
/// Minimum length is Identity's own rule (Auth:Password:MinLength).
/// </summary>
public sealed class PolicyPasswordValidator(AppDbContext db, IOptions<AuthOptions> options, IPasswordHasher<ApplicationUser> hasher) : IPasswordValidator<ApplicationUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var o = options.Value.Password;
        var errors = new List<IdentityError>();
        password ??= "";
        if (password.Length > o.MaxLength) errors.Add(new() { Code = "PasswordTooLong", Description = $"Passwords can be at most {o.MaxLength} characters." });
        if (password.Distinct().Count() < 4) errors.Add(new() { Code = "PasswordTooSimple", Description = "Use at least four different characters." });
        if (o.BlockCommon && CommonPasswords.Contains(password))
            errors.Add(new() { Code = "PasswordCommon", Description = "This password appears in lists of breached or commonly used passwords. Choose something less predictable — a few unrelated words work well." });
        var lower = password.ToLowerInvariant();
        var local = (user.Email ?? "").Split('@')[0].ToLowerInvariant();
        if (local.Length >= 4 && lower.Contains(local)) errors.Add(new() { Code = "PasswordContainsEmail", Description = "The password must not contain your email name." });
        if (o.HistoryCount > 0 && user.Id != Guid.Empty)
        {
            var hashes = await db.PasswordHistory.AsNoTracking().Where(h => h.UserId == user.Id).OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id)
                .Take(o.HistoryCount).Select(h => h.PasswordHash).ToListAsync();
            if (user.PasswordHash is { } current && !hashes.Contains(current)) hashes.Insert(0, current);
            if (hashes.Take(o.HistoryCount).Any(h => hasher.VerifyHashedPassword(user, h, password) != PasswordVerificationResult.Failed))
                errors.Add(new() { Code = "PasswordReused", Description = $"Choose a password you have not used before (the last {o.HistoryCount} cannot be reused)." });
        }
        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }
}

/// <summary>
/// Identity user store that records each new password hash (for the reuse rule) and when the password last changed. Every
/// path that sets a password (create, change, reset, add) ends in SetPasswordHashAsync, so nothing can bypass the history.
/// </summary>
public sealed class AppUserStore(AppDbContext context, IOptions<AuthOptions> auth, IClock clock, IdentityErrorDescriber? describer = null)
    : Microsoft.AspNetCore.Identity.EntityFrameworkCore.UserStore<ApplicationUser, IdentityRole<Guid>, AppDbContext, Guid>(context, describer)
{
    public override async Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken = default)
    {
        await base.SetPasswordHashAsync(user, passwordHash, cancellationToken);
        if (passwordHash is null) return;
        var isNew = user.Id == Guid.Empty;
        if (isNew) user.Id = Guid.CreateVersion7(); // so the history row can reference the account being created
        var now = clock.UtcNow;
        user.PasswordChangedAt = now;
        Context.PasswordHistory.Add(new PasswordHistoryEntry { UserId = user.Id, PasswordHash = passwordHash, CreatedAt = now });
        if (!isNew)
        {
            var keep = Math.Max(1, auth.Value.Password.HistoryCount);
            var old = await Context.PasswordHistory.Where(h => h.UserId == user.Id).OrderByDescending(h => h.CreatedAt).ThenByDescending(h => h.Id).Skip(keep - 1).ToListAsync(cancellationToken);
            Context.PasswordHistory.RemoveRange(old);
        }
    }
}
