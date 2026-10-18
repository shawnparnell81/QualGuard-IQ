using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

/// <summary>
/// Relationship mapping (TECH mapping workbench, relationships): turns the text a file carries for a site, department, user, supplier, customer,
/// product, part, equipment or existing record into the id of the existing record. Matches codes and numbers exactly (ignoring case), then names.
/// Values that match nothing are reported as missing references; nothing is created on the fly.
/// </summary>
public sealed class ReferenceResolver(AppDbContext db)
{
    readonly Dictionary<(string, string, Guid), Guid?> cache = [];

    public static readonly IReadOnlyDictionary<string, string> Describe = new Dictionary<string, string>
    {
        ["site"] = "site code or name", ["department"] = "department code or name at the import's site", ["user"] = "e-mail address or full name",
        ["supplier"] = "supplier code or name", ["customer"] = "customer code or name", ["product"] = "product number or name", ["part"] = "part number",
        ["equipment"] = "equipment number, asset number or serial number", ["record"] = "record number (NCR, CAPA, document, form record …)",
    };

    public async Task<Guid?> ResolveAsync(string type, string value, Guid siteId, CancellationToken ct)
    {
        var v = value.Trim();
        if (v.Length == 0) return null;
        if (Guid.TryParse(v, out var g)) return g;
        var key = (type, v.ToLowerInvariant(), siteId);
        if (cache.TryGetValue(key, out var hit)) return hit;
        var l = v.ToLowerInvariant();
        Guid? id = type switch
        {
            "site" => await db.Sites.AsNoTracking().Where(s => !s.IsArchived && (s.Code.ToLower() == l || s.Name.ToLower() == l)).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct),
            "department" => await db.Departments.AsNoTracking().Where(d => d.SiteId == siteId && (d.Code.ToLower() == l || d.Name.ToLower() == l)).Select(d => (Guid?)d.Id).FirstOrDefaultAsync(ct),
            "user" => await db.Users.AsNoTracking().Where(u => u.IsActive && ((u.Email != null && u.Email.ToLower() == l) || u.DisplayName.ToLower() == l)).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct),
            "supplier" => await db.Suppliers.AsNoTracking().Where(s => s.Code.ToLower() == l || s.LegalName.ToLower() == l || (s.OperatingName != null && s.OperatingName.ToLower() == l)).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct),
            "customer" => await db.Customers.AsNoTracking().Where(c => c.Code.ToLower() == l || c.Name.ToLower() == l).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct),
            "product" => await db.Products.AsNoTracking().Where(p => p.Number.ToLower() == l || p.Name.ToLower() == l).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct),
            "part" => await db.Parts.AsNoTracking().Where(p => p.PartNumber.ToLower() == l).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct),
            "equipment" => await db.Equipment.AsNoTracking().Where(e => e.Number.ToLower() == l || (e.AssetNumber != null && e.AssetNumber.ToLower() == l) || (e.SerialNumber != null && e.SerialNumber.ToLower() == l))
                .OrderBy(e => e.SiteId == siteId ? 0 : 1).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct),
            "record" => await db.SearchEntries.AsNoTracking().Where(s => s.Number.ToLower() == l).Select(s => (Guid?)s.RecordId).FirstOrDefaultAsync(ct),
            _ => null,
        };
        cache[key] = id;
        return id;
    }
}
