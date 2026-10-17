namespace QualGuardIQ.Application.Records;

/// <summary>What the shared engines need to know about any record, whatever module owns it.</summary>
public sealed record RecordInfo(
    string Type, Guid Id, Guid SiteId, Guid? DepartmentId, string Number, string Title, string? Status,
    string ViewPermission, string EditPermission, string Url, bool IsLocked = false);

/// <summary>Each module registers one provider per record type it owns.</summary>
public interface IRecordTypeProvider
{
    string Type { get; }
    Task<RecordInfo?> FindAsync(Guid id, CancellationToken ct);
}

/// <summary>Called by the workflow engine when an approval request finishes, so the owning module can change state.</summary>
public interface IApprovalHandler
{
    string RecordType { get; }
    Task OnApprovedAsync(Guid recordId, CancellationToken ct);
    Task OnRejectedAsync(Guid recordId, string? comment, CancellationToken ct);
}

public interface IRecordRegistry
{
    Task<RecordInfo> GetAsync(string type, Guid id, CancellationToken ct = default);
    /// <summary>Loads the record and throws 404 when it does not exist or the current user may not view it.</summary>
    Task<RecordInfo> RequireViewAsync(string type, Guid id, CancellationToken ct = default);
    /// <summary>Loads the record and throws 403 when the current user may not edit it.</summary>
    Task<RecordInfo> RequireEditAsync(string type, Guid id, CancellationToken ct = default);
    bool IsKnownType(string type);
    /// <summary>Every registered record type identifier.</summary>
    IReadOnlyCollection<string> KnownTypes { get; }
}

public interface ISearchIndexer
{
    Task IndexAsync(RecordInfo record, string? summary, CancellationToken ct = default);
    Task RemoveAsync(string type, Guid id, CancellationToken ct = default);
}

public interface INotifier
{
    Task NotifyAsync(IEnumerable<Guid> userIds, string title, string? body, RecordInfo? record, bool email = true, CancellationToken ct = default);
}
