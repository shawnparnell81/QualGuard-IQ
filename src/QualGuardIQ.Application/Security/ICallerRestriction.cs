namespace QualGuardIQ.Application.Security;

/// <summary>
/// Narrows the current caller's permissions below its user's roles. An API client or MES device acts as its service user but
/// only with the client's permission scopes and sites. Null sets mean "not restricted". Applies only to <see cref="UserId"/>.
/// </summary>
public interface ICallerRestriction
{
    Guid? UserId { get; }
    IReadOnlySet<string>? Scopes { get; }
    IReadOnlySet<Guid>? Sites { get; }
}

public sealed class NoCallerRestriction : ICallerRestriction
{
    public Guid? UserId => null;
    public IReadOnlySet<string>? Scopes => null;
    public IReadOnlySet<Guid>? Sites => null;
}

public static class CallerRestrictionExtensions
{
    /// <summary>Grants left after the restriction: scopes filter permissions; an all-sites grant becomes one grant per allowed site.</summary>
    public static IReadOnlyList<PermissionGrant> Apply(this ICallerRestriction r, Guid userId, IReadOnlyList<PermissionGrant> grants)
    {
        if (r.UserId != userId || (r.Scopes is null && r.Sites is null)) return grants;
        var outp = new List<PermissionGrant>();
        foreach (var g in grants)
        {
            if (r.Scopes is { } sc && !sc.Contains(g.Permission)) continue;
            if (r.Sites is not { } sites) { outp.Add(g); continue; }
            if (g.SiteId is null) outp.AddRange(sites.Select(s => new PermissionGrant(g.Permission, s, g.DepartmentId)));
            else if (sites.Contains(g.SiteId.Value)) outp.Add(g);
        }
        return outp.Distinct().ToList();
    }
}
