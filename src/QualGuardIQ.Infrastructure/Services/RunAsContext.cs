namespace QualGuardIQ.Infrastructure.Services;

/// <summary>
/// Per-scope identity for background work done on someone's behalf (a scheduled report runs with its owner's permissions, never more).
/// Set only by trusted server code in a fresh DI scope; requests never set it.
/// </summary>
public sealed class RunAsContext
{
    public Guid? UserId { get; private set; }
    public string? UserName { get; private set; }
    public void Set(Guid userId, string userName) { if (UserId is not null) throw new InvalidOperationException("Run-as identity is already set for this scope."); UserId = userId; UserName = userName; }
}
