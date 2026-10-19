using Microsoft.AspNetCore.Identity;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Api.Infrastructure;

/// <summary>A throwaway password hash, made once per process, so a sign-in for an unknown account costs the same as a real one.</summary>
public static class PasswordTiming
{
    static string? _hash;

    public static string DummyHash(IPasswordHasher<ApplicationUser> hasher) =>
        _hash ??= hasher.HashPassword(new ApplicationUser(), Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)));
}
