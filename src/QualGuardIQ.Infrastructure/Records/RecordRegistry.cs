using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;

namespace QualGuardIQ.Infrastructure.Records;

public sealed class RecordRegistry(IEnumerable<IRecordTypeProvider> providers, IAccessService access, ICurrentUser user) : IRecordRegistry
{
    private readonly Dictionary<string, IRecordTypeProvider> _providers = providers.ToDictionary(p => p.Type, StringComparer.OrdinalIgnoreCase);

    public bool IsKnownType(string type) => _providers.ContainsKey(type);
    public IReadOnlyCollection<string> KnownTypes => _providers.Keys;

    public async Task<RecordInfo> GetAsync(string type, Guid id, CancellationToken ct = default)
    {
        if (!_providers.TryGetValue(type, out var p)) throw new NotFoundException("Record type");
        return await p.FindAsync(id, ct) ?? throw new NotFoundException("Record");
    }

    public async Task<RecordInfo> RequireViewAsync(string type, Guid id, CancellationToken ct = default)
    {
        var r = await GetAsync(type, id, ct);
        if (user.UserId is not { } uid || !await access.CanAccessAsync(uid, r.ViewPermission, r.SiteId, r.DepartmentId, ct))
            throw new NotFoundException("Record");
        return r;
    }

    public async Task<RecordInfo> RequireEditAsync(string type, Guid id, CancellationToken ct = default)
    {
        var r = await RequireViewAsync(type, id, ct);
        if (!await access.CanAccessAsync(user.UserId!.Value, r.EditPermission, r.SiteId, r.DepartmentId, ct))
            throw new ForbiddenException();
        return r;
    }
}
