namespace QualGuardIQ.Domain.Common;

/// <summary>
/// Records whose content becomes immutable once they reach a locked state (for example an approved revision).
/// Enforced centrally when changes are saved; only the listed properties may change afterwards.
/// </summary>
public interface IContentLock
{
    bool IsLockedState(object? originalStatus);
    string StatusProperty { get; }
    IReadOnlyCollection<string> MutableWhenLocked { get; }
}
