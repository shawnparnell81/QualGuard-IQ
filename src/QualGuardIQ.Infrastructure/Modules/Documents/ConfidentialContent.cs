using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Infrastructure.Modules.Documents;

/// <summary>
/// Confidentiality markings on form templates: content marked Confidential or Restricted is visible only to people holding
/// content.confidential for its site or department, and to its owner. Public and Internal content follows the normal view permission.
/// </summary>
public static class ConfidentialContent
{
    public static readonly string[] Marked = ["Confidential", "Restricted"];

    public static bool IsMarked(string? marking) => marking is "Confidential" or "Restricted";

    public static async Task<IQueryable<ControlledDocument>> VisibleToAsync(this IQueryable<ControlledDocument> q, IAccessService access, Guid me, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(me, Permissions.ContentConfidential, ct);
        if (scope.AllSites) return q;
        var marked = Marked;
        if (scope.IsEmpty) return q.Where(d => !marked.Contains(d.Confidentiality) || d.OwnerId == me);
        var sites = scope.SiteIds.ToList();
        var pairs = scope.DepartmentPairs.Select(p => p.Site.ToString() + "|" + p.Dept.ToString()).ToList();
        return q.Where(d => !marked.Contains(d.Confidentiality) || d.OwnerId == me || sites.Contains(d.SiteId)
            || (d.DepartmentId != null && pairs.Contains(d.SiteId.ToString() + "|" + d.DepartmentId.ToString())));
    }

    public static async Task<bool> CanSeeAsync(IAccessService access, Guid me, ControlledDocument d, CancellationToken ct) =>
        !IsMarked(d.Confidentiality) || d.OwnerId == me || await access.CanAccessAsync(me, Permissions.ContentConfidential, d.SiteId, d.DepartmentId, ct);
}
