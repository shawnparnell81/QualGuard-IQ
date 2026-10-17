using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Inventory;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Logistics;

public sealed record TemplateInput(string Code, string Name, ShippingDocumentKind Kind, string? Description, Guid? SiteId);
public sealed record TemplateUpdate(string Name, string? Description, bool Active);
public sealed record RevisionInput(DocLayout Layout, Guid? CustomerId, Guid? CarrierId, int Priority, string? ChangeSummary, DateTimeOffset? EffectiveFrom);
public sealed record ApproveRevisionInput(string Notes, DateTimeOffset? EffectiveFrom);
public sealed record RejectRevisionInput(string Reason);
public sealed record ShippingTermsInput(string? FreightTerms, string? SpecialInstructions);
public sealed record RevisionRow(Guid Id, int Revision, TemplateRevisionStatus Status, string State, DocLayout Layout, bool HasLogo, Guid? CustomerId, string? Customer, Guid? CarrierId, string? Carrier, int Priority,
    string? ChangeSummary, DateTimeOffset CreatedAt, string? CreatedBy, DateTimeOffset? SubmittedAt, string? SubmittedBy, DateTimeOffset? ApprovedAt, string? ApprovedBy, string? ApprovalNotes,
    string? RejectedReason, DateTimeOffset? EffectiveFrom, string? ContentHash);
public sealed record TemplateRow(Guid Id, string Code, string Name, ShippingDocumentKind Kind, string? Description, Guid? SiteId, string? Site, bool Active, int? EffectiveRevision, int? PendingRevision,
    int? DraftRevision, string? Customer, string? Carrier, int? Priority, bool CanEdit, bool CanApprove);
public sealed record TemplateDetail(TemplateRow Template, List<RevisionRow> Revisions);
public sealed record GeneratedRow(Guid Id, ShippingDocumentKind Kind, string Reference, string? TemplateCode, int? TemplateRevision, bool BuiltIn, int Pages, string PdfSha256, DateTimeOffset GeneratedAt, string? GeneratedBy);
public sealed record ReproduceResult(bool Identical, string StoredSha256, string ReproducedSha256, string? TemplateCode, int? TemplateRevision, bool BuiltIn);
public sealed record TemplateChoice(ShippingDocumentKind Kind, Guid? TemplateId, string? TemplateCode, string? TemplateName, int? Revision, bool BuiltIn, string Reason);
public sealed record SectionCatalog(IReadOnlyList<SectionInfo> Sections, IReadOnlyList<string> Placeholders, Dictionary<string, DocLayout> BuiltIn);

/// <summary>
/// Shipping document templates (bill of lading, packing list, manifest): revisions with draft → approval → effective date, selection by
/// site, customer, carrier and priority, preview, and issued documents tied to the revision and data snapshot that produced them.
/// </summary>
public sealed class ShippingDocumentService(AppDbContext db, ModuleSupport m, ShippingService shipping)
{
    public const int MaxLogoBytes = 512 * 1024;

    public SectionCatalog Catalog() => new(ShippingDocumentLayouts.Sections, ShippingDocumentLayouts.Placeholders,
        Enum.GetValues<ShippingDocumentKind>().ToDictionary(k => k.ToString(), ShippingDocumentLayouts.BuiltIn));

    // ---------- permissions ----------
    async Task<bool> CanAtAsync(string perm, Guid? siteId, CancellationToken ct) => siteId is { } s ? await m.CanAsync(perm, s, null, ct) : await m.HasAllSitesAsync(perm, ct);
    async Task RequireAtAsync(string perm, Guid? siteId, CancellationToken ct) { if (!await CanAtAsync(perm, siteId, ct)) throw new ForbiddenException(siteId is null ? "Organization-wide templates need this permission at every site." : "You do not have permission for this action."); }
    async Task<bool> CanSeeAsync(Guid? siteId, CancellationToken ct) => siteId is { } s ? await m.CanAsync(Permissions.ShippingView, s, null, ct) : await m.CanAnyAsync(Permissions.ShippingView, ct);

    // ---------- templates ----------
    public async Task<List<TemplateRow>> TemplatesAsync(ShippingDocumentKind? kind, CancellationToken ct)
    {
        var q = db.ShippingDocumentTemplates.AsNoTracking();
        if (kind is { } k) q = q.Where(x => x.Kind == k);
        var list = await q.OrderBy(x => x.Kind).ThenBy(x => x.Code).ToListAsync(ct);
        var rows = new List<TemplateRow>();
        foreach (var t in list) if (await CanSeeAsync(t.SiteId, ct)) rows.Add(await RowAsync(t, null, ct));
        return rows;
    }

    public async Task<TemplateDetail> TemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await db.ShippingDocumentTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Template");
        if (!await CanSeeAsync(t.SiteId, ct)) throw new NotFoundException("Template");
        var revs = await db.ShippingDocumentTemplateRevisions.AsNoTracking().Where(x => x.TemplateId == id).OrderByDescending(x => x.Revision).ToListAsync(ct);
        var userIds = revs.SelectMany(r => new[] { r.CreatedById, r.SubmittedById, r.ApprovedById }).OfType<Guid>().Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var custIds = revs.Select(r => r.CustomerId).OfType<Guid>().Distinct().ToList(); var carIds = revs.Select(r => r.CarrierId).OfType<Guid>().Distinct().ToList();
        var custs = await db.Customers.AsNoTracking().Where(x => custIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var cars = await db.Carriers.AsNoTracking().Where(x => carIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        string? U(Guid? g) => g is { } v ? users.GetValueOrDefault(v) : null;
        var effective = Effective(revs, m.Now);
        return new(await RowAsync(t, revs, ct), revs.Select(r => new RevisionRow(r.Id, r.Revision, r.Status, State(r, effective, m.Now), ShippingDocumentLayouts.Parse(r.LayoutJson), r.Logo is { Length: > 0 },
            r.CustomerId, r.CustomerId is { } c ? custs.GetValueOrDefault(c) : null, r.CarrierId, r.CarrierId is { } cr ? cars.GetValueOrDefault(cr) : null, r.Priority, r.ChangeSummary, r.CreatedAt, U(r.CreatedById),
            r.SubmittedAt, U(r.SubmittedById), r.ApprovedAt, U(r.ApprovedById), r.ApprovalNotes, r.RejectedReason, r.EffectiveFrom, r.ContentHash)).ToList());
    }

    static ShippingDocumentTemplateRevision? Effective(IEnumerable<ShippingDocumentTemplateRevision> revs, DateTimeOffset at) =>
        revs.Where(r => r.Status == TemplateRevisionStatus.Approved && r.EffectiveFrom <= at).OrderByDescending(r => r.Revision).FirstOrDefault();

    static string State(ShippingDocumentTemplateRevision r, ShippingDocumentTemplateRevision? effective, DateTimeOffset now) => r.Status switch
    {
        TemplateRevisionStatus.Draft => "Draft", TemplateRevisionStatus.PendingApproval => "Pending approval", TemplateRevisionStatus.Rejected => "Rejected",
        _ => effective?.Id == r.Id ? "Effective" : r.EffectiveFrom > now ? "Scheduled" : "Superseded",
    };

    async Task<TemplateRow> RowAsync(ShippingDocumentTemplate t, List<ShippingDocumentTemplateRevision>? revs, CancellationToken ct)
    {
        revs ??= await db.ShippingDocumentTemplateRevisions.AsNoTracking().Where(x => x.TemplateId == t.Id).ToListAsync(ct);
        var eff = Effective(revs, m.Now);
        var site = t.SiteId is { } s ? await db.Sites.AsNoTracking().Where(x => x.Id == s).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var cust = eff?.CustomerId is { } c ? await db.Customers.AsNoTracking().Where(x => x.Id == c).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var car = eff?.CarrierId is { } cr ? await db.Carriers.AsNoTracking().Where(x => x.Id == cr).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return new(t.Id, t.Code, t.Name, t.Kind, t.Description, t.SiteId, site, t.Active, eff?.Revision,
            revs.FirstOrDefault(r => r.Status == TemplateRevisionStatus.PendingApproval)?.Revision, revs.FirstOrDefault(r => r.Status == TemplateRevisionStatus.Draft)?.Revision,
            cust, car, eff?.Priority, await CanAtAsync(Permissions.ShippingEdit, t.SiteId, ct), await CanAtAsync(Permissions.ShippingApprove, t.SiteId, ct));
    }

    public async Task<Guid> CreateAsync(TemplateInput r, CancellationToken ct)
    {
        await RequireAtAsync(Permissions.ShippingEdit, r.SiteId, ct);
        var code = (r.Code ?? "").Trim().ToUpperInvariant();
        if (code.Length is < 2 or > 40 || !code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')) throw new AppException("The code must be 2–40 letters, digits, - or _.");
        ModuleSupport.Required(r.Name, "Name", 200);
        if (r.Description?.Length > 2000) throw new AppException("The description is longer than 2,000 characters.");
        if (!Enum.IsDefined(r.Kind)) throw new AppException("Unknown document type.");
        if (r.SiteId is { } s) await m.ValidateLocationAsync(s, null, ct);
        if (await db.ShippingDocumentTemplates.AnyAsync(x => x.Code == code, ct)) throw new ConflictException($"A template with code {code} already exists.");
        var t = new ShippingDocumentTemplate { Code = code, Name = r.Name.Trim(), Kind = r.Kind, Description = r.Description?.Trim(), SiteId = r.SiteId };
        db.ShippingDocumentTemplates.Add(t);
        db.ShippingDocumentTemplateRevisions.Add(new ShippingDocumentTemplateRevision { TemplateId = t.Id, Revision = 1, Status = TemplateRevisionStatus.Draft,
            LayoutJson = ShippingDocumentLayouts.Serialize(ShippingDocumentLayouts.BuiltIn(r.Kind)), ChangeSummary = "First revision." });
        await db.SaveChangesAsync(ct);
        return t.Id;
    }

    async Task<ShippingDocumentTemplate> EditTemplateAsync(Guid id, CancellationToken ct)
    {
        var t = await db.ShippingDocumentTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Template");
        if (!await CanSeeAsync(t.SiteId, ct)) throw new NotFoundException("Template");
        await RequireAtAsync(Permissions.ShippingEdit, t.SiteId, ct);
        return t;
    }

    public async Task UpdateAsync(Guid id, TemplateUpdate r, CancellationToken ct)
    {
        var t = await EditTemplateAsync(id, ct);
        ModuleSupport.Required(r.Name, "Name", 200);
        if (r.Description?.Length > 2000) throw new AppException("The description is longer than 2,000 characters.");
        if (r.Active && !t.Active)
        {
            var eff = Effective(await db.ShippingDocumentTemplateRevisions.AsNoTracking().Where(x => x.TemplateId == id).ToListAsync(ct), m.Now);
            if (eff is not null) await RefuseOverlapAsync(t, eff.CustomerId, eff.CarrierId, eff.Priority, ct);
        }
        t.Name = r.Name.Trim(); t.Description = r.Description?.Trim(); t.Active = r.Active;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Two active templates of one type with the same site, customer, carrier and priority would make the choice arbitrary.</summary>
    async Task RefuseOverlapAsync(ShippingDocumentTemplate t, Guid? customerId, Guid? carrierId, int priority, CancellationToken ct)
    {
        var others = await db.ShippingDocumentTemplates.AsNoTracking().Where(x => x.Id != t.Id && x.Active && x.Kind == t.Kind && x.SiteId == t.SiteId).ToListAsync(ct);
        if (others.Count == 0) return;
        var ids = others.Select(o => o.Id).ToList();
        var revs = await db.ShippingDocumentTemplateRevisions.AsNoTracking().Where(x => ids.Contains(x.TemplateId) && x.Status == TemplateRevisionStatus.Approved).ToListAsync(ct);
        foreach (var o in others)
        {
            var latest = revs.Where(r => r.TemplateId == o.Id).OrderByDescending(r => r.Revision).FirstOrDefault();
            if (latest is not null && latest.CustomerId == customerId && latest.CarrierId == carrierId && latest.Priority == priority)
                throw new ConflictException($"Template {o.Code} already applies to the same site, customer, carrier and priority. Change the priority or the customer/carrier, or deactivate {o.Code}.");
        }
    }

    // ---------- revisions ----------
    async Task<(ShippingDocumentTemplate T, ShippingDocumentTemplateRevision R)> RevAsync(Guid revId, bool edit, CancellationToken ct)
    {
        var r = await db.ShippingDocumentTemplateRevisions.FirstOrDefaultAsync(x => x.Id == revId, ct) ?? throw new NotFoundException("Template revision");
        var t = await db.ShippingDocumentTemplates.FirstAsync(x => x.Id == r.TemplateId, ct);
        if (!await CanSeeAsync(t.SiteId, ct)) throw new NotFoundException("Template revision");
        if (edit) await RequireAtAsync(Permissions.ShippingEdit, t.SiteId, ct);
        return (t, r);
    }

    public async Task<Guid> NewRevisionAsync(Guid templateId, CancellationToken ct)
    {
        var t = await EditTemplateAsync(templateId, ct);
        var revs = await db.ShippingDocumentTemplateRevisions.Where(x => x.TemplateId == t.Id).OrderByDescending(x => x.Revision).ToListAsync(ct);
        if (revs.FirstOrDefault(r => r.Status is TemplateRevisionStatus.Draft or TemplateRevisionStatus.PendingApproval) is { } open)
            throw new ConflictException($"Revision {open.Revision} is still {(open.Status == TemplateRevisionStatus.Draft ? "a draft" : "pending approval")}; finish it first.");
        var src = revs.FirstOrDefault(r => r.Status == TemplateRevisionStatus.Approved) ?? revs[0];
        var n = new ShippingDocumentTemplateRevision { TemplateId = t.Id, Revision = revs[0].Revision + 1, Status = TemplateRevisionStatus.Draft, LayoutJson = src.LayoutJson, Logo = src.Logo,
            CustomerId = src.CustomerId, CarrierId = src.CarrierId, Priority = src.Priority };
        db.ShippingDocumentTemplateRevisions.Add(n);
        await db.SaveChangesAsync(ct);
        return n.Id;
    }

    static void RequireDraft(ShippingDocumentTemplateRevision r)
    {
        if (r.Status != TemplateRevisionStatus.Draft) throw new ConflictException($"Revision {r.Revision} is {r.Status}; only a draft can be changed. Start a new revision.");
    }

    async Task ValidateRuleAsync(ShippingDocumentTemplate t, Guid? customerId, Guid? carrierId, int priority, CancellationToken ct)
    {
        if (priority is < 1 or > 1000) throw new AppException("Priority must be 1–1000 (1 is tried first).");
        if (customerId is { } c)
        {
            if (t.Kind == ShippingDocumentKind.ShippingManifest) throw new AppException("A manifest covers many customers; it can be specific to a site and carrier only.");
            var cu = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == c, ct) ?? throw new AppException("Unknown customer.");
            if (t.SiteId is { } s && cu.SiteId != s) throw new AppException("The customer belongs to another site than this template.");
        }
        if (carrierId is { } cr && !await db.Carriers.AnyAsync(x => x.Id == cr, ct)) throw new AppException("Unknown carrier.");
    }

    public async Task SaveRevisionAsync(Guid revId, RevisionInput r, CancellationToken ct)
    {
        var (t, rev) = await RevAsync(revId, true, ct);
        RequireDraft(rev);
        var errors = ShippingDocumentLayouts.Validate(r.Layout, t.Kind);
        if (errors.Count > 0) throw new AppException(string.Join(" ", errors));
        await ValidateRuleAsync(t, r.CustomerId, r.CarrierId, r.Priority, ct);
        if (r.ChangeSummary?.Length > 2000) throw new AppException("The change summary is longer than 2,000 characters.");
        rev.LayoutJson = ShippingDocumentLayouts.Serialize(r.Layout); rev.CustomerId = r.CustomerId; rev.CarrierId = r.CarrierId; rev.Priority = r.Priority;
        rev.ChangeSummary = r.ChangeSummary?.Trim(); rev.EffectiveFrom = r.EffectiveFrom;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetLogoAsync(Guid revId, byte[]? jpeg, CancellationToken ct)
    {
        var (_, rev) = await RevAsync(revId, true, ct);
        RequireDraft(rev);
        if (jpeg is not null)
        {
            if (jpeg.Length > MaxLogoBytes) throw new AppException("The logo must be at most 512 KB.");
            if (PdfCanvas.JpegSize(jpeg) is null) throw new AppException("The logo must be a JPEG image (save PNG or SVG logos as JPEG first).");
        }
        rev.Logo = jpeg;
        await db.SaveChangesAsync(ct);
    }

    public async Task<byte[]> LogoAsync(Guid revId, CancellationToken ct)
    {
        var (_, rev) = await RevAsync(revId, false, ct);
        return rev.Logo is { Length: > 0 } l ? l : throw new NotFoundException("Logo");
    }

    public async Task SubmitAsync(Guid revId, CancellationToken ct)
    {
        var (t, rev) = await RevAsync(revId, true, ct);
        RequireDraft(rev);
        var errors = ShippingDocumentLayouts.Validate(ShippingDocumentLayouts.Parse(rev.LayoutJson), t.Kind);
        if (errors.Count > 0) throw new AppException(string.Join(" ", errors));
        if (string.IsNullOrWhiteSpace(rev.ChangeSummary)) throw new AppException("Describe the change before submitting it for approval.");
        if (t.Active) await RefuseOverlapAsync(t, rev.CustomerId, rev.CarrierId, rev.Priority, ct);
        rev.Status = TemplateRevisionStatus.PendingApproval; rev.SubmittedById = m.UserId; rev.SubmittedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task ApproveAsync(Guid revId, ApproveRevisionInput r, CancellationToken ct)
    {
        var (t, rev) = await RevAsync(revId, false, ct);
        await RequireAtAsync(Permissions.ShippingApprove, t.SiteId, ct);
        if (rev.Status != TemplateRevisionStatus.PendingApproval) throw new ConflictException("Only a revision pending approval can be approved.");
        if (rev.SubmittedById == m.UserId || rev.CreatedById == m.UserId) throw new ForbiddenException("The person who prepared or submitted a revision cannot approve it.");
        if (string.IsNullOrWhiteSpace(r.Notes) || r.Notes.Trim().Length < 5) throw new AppException("Approval notes are required (at least 5 characters).");
        if (t.Active) await RefuseOverlapAsync(t, rev.CustomerId, rev.CarrierId, rev.Priority, ct);
        var from = new[] { m.Now, rev.EffectiveFrom ?? m.Now, r.EffectiveFrom ?? m.Now }.Max();
        rev.Status = TemplateRevisionStatus.Approved; rev.ApprovedById = m.UserId; rev.ApprovedAt = m.Now; rev.ApprovalNotes = r.Notes.Trim(); rev.EffectiveFrom = from;
        rev.ContentHash = Hash(rev);
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid revId, RejectRevisionInput r, CancellationToken ct)
    {
        var (t, rev) = await RevAsync(revId, false, ct);
        await RequireAtAsync(Permissions.ShippingApprove, t.SiteId, ct);
        if (rev.Status != TemplateRevisionStatus.PendingApproval) throw new ConflictException("Only a revision pending approval can be rejected.");
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length < 5) throw new AppException("A reason is required (at least 5 characters).");
        rev.Status = TemplateRevisionStatus.Rejected; rev.RejectedReason = r.Reason.Trim();
        await db.SaveChangesAsync(ct);
    }

    static string Hash(ShippingDocumentTemplateRevision r)
    {
        var canonical = ShippingDocumentLayouts.Serialize(ShippingDocumentLayouts.Parse(r.LayoutJson));
        var s = $"{canonical}|{(r.Logo is { Length: > 0 } l ? Convert.ToHexString(SHA256.HashData(l)) : "")}|{r.CustomerId}|{r.CarrierId}|{r.Priority}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    }

    static DocLayout Approved(ShippingDocumentTemplate t, ShippingDocumentTemplateRevision r)
    {
        if (r.ContentHash is null || r.ContentHash != Hash(r)) throw new ConflictException($"Template {t.Code} revision {r.Revision} no longer matches what was approved; it cannot be used. Approve a new revision.");
        return ShippingDocumentLayouts.Parse(r.LayoutJson);
    }

    /// <summary>Preview a revision (any state) with sample data or a shipment the caller can see. Marked as a preview on every page.</summary>
    public async Task<byte[]> PreviewAsync(Guid revId, Guid? shipmentId, CancellationToken ct)
    {
        var (t, rev) = await RevAsync(revId, false, ct);
        var data = shipmentId is { } sid && t.Kind != ShippingDocumentKind.ShippingManifest ? await ShipmentDataAsync(sid, t.Kind, preview: true, ct) : ShippingDocumentLayouts.Sample(t.Kind);
        return ShippingDocumentRenderer.Render(ShippingDocumentLayouts.Parse(rev.LayoutJson), data, rev.Logo, $"PREVIEW - {t.Code} revision {rev.Revision} ({State(rev, null, m.Now)}) - not for shipment").Pdf;
    }

    // ---------- selection ----------
    async Task<(ShippingDocumentTemplate T, ShippingDocumentTemplateRevision R, string Reason)?> SelectAsync(ShippingDocumentKind kind, Guid siteId, Guid? customerId, Guid? carrierId, CancellationToken ct)
    {
        var templates = await db.ShippingDocumentTemplates.AsNoTracking().Where(x => x.Active && x.Kind == kind && (x.SiteId == null || x.SiteId == siteId)).ToListAsync(ct);
        if (templates.Count == 0) return null;
        var ids = templates.Select(x => x.Id).ToList();
        var revs = await db.ShippingDocumentTemplateRevisions.AsNoTracking().Where(x => ids.Contains(x.TemplateId) && x.Status == TemplateRevisionStatus.Approved).ToListAsync(ct);
        var best = templates.Select(t => (T: t, R: Effective(revs.Where(r => r.TemplateId == t.Id), m.Now)))
            .Where(x => x.R is not null && (x.R.CustomerId is null || x.R.CustomerId == customerId) && (x.R.CarrierId is null || x.R.CarrierId == carrierId))
            .OrderByDescending(x => (x.R!.CustomerId is null ? 0 : 4) + (x.R.CarrierId is null ? 0 : 2) + (x.T.SiteId is null ? 0 : 1)).ThenBy(x => x.R!.Priority).ThenBy(x => x.T.Code, StringComparer.Ordinal)
            .FirstOrDefault();
        if (best.R is null) return null;
        var why = string.Join(", ", new[] { best.R.CustomerId is null ? null : "customer", best.R.CarrierId is null ? null : "carrier", best.T.SiteId is null ? null : "site" }.OfType<string>());
        return (best.T, best.R, why.Length == 0 ? "Organization default" : "Matches " + why);
    }

    public async Task<List<TemplateChoice>> ChoicesAsync(Guid shipmentId, CancellationToken ct)
    {
        await m.ViewAsync(ShippingService.ShipmentType, shipmentId, ct);
        var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == shipmentId, ct);
        var res = new List<TemplateChoice>();
        foreach (var k in new[] { ShippingDocumentKind.PackingList, ShippingDocumentKind.BillOfLading })
            res.Add(await SelectAsync(k, s.SiteId, s.CustomerId, s.CarrierId, ct) is { } x
                ? new(k, x.T.Id, x.T.Code, x.T.Name, x.R.Revision, false, x.Reason)
                : new(k, null, null, null, null, true, "No approved template applies; the built-in layout is used."));
        return res;
    }

    // ---------- generation ----------
    async Task<DocData> ShipmentDataAsync(Guid shipmentId, ShippingDocumentKind kind, bool preview, CancellationToken ct)
    {
        var d = await shipping.GetAsync(shipmentId, ct);
        var s = d.Shipment;
        if (kind == ShippingDocumentKind.BillOfLading && s.BolNumber is null && !preview) throw new ConflictException("The bill of lading is issued when the shipment ships.");
        var site = await db.Sites.AsNoTracking().FirstAsync(x => x.Id == s.SiteId, ct);
        var zone = await m.ZoneAsync(s.SiteId, ct);
        var cust = s.CustomerId is { } cid ? await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == cid, ct) : null;
        var car = s.CarrierId is { } crid ? await db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == crid, ct) : null;
        var partIds = d.Lines.Select(l => l.Line.PartId).Distinct().ToList();
        var uoms = await db.Parts.AsNoTracking().Where(x => partIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.UnitOfMeasure, ct);
        var byId = d.Containers.ToDictionary(c => c.Container.Id, c => c.Container.Number);
        string Local(DateTimeOffset t, string f) => TimeZoneInfo.ConvertTime(t, zone).ToString(f, CultureInfo.InvariantCulture);
        static List<string> Split(string? a) => (a ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var shipTo = s.ShipToLine1 is not null
            ? new DocParty(s.ShipToName ?? cust?.Name, new[] { s.ShipToLine1, s.ShipToLine2, string.Join(" ", new[] { s.ShipToCity, s.ShipToRegion, s.ShipToPostalCode }.Where(x => !string.IsNullOrWhiteSpace(x))), s.ShipToCountry, s.ShipToPhone }
                .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList())
            : new DocParty(cust?.Name, Split(s.ShipTo).Concat(s.ShipTo is null ? Split(cust?.Address) : []).ToList());
        return new DocData(kind, kind == ShippingDocumentKind.BillOfLading ? s.BolNumber ?? s.Number : s.Number, s.Number, s.BolNumber, s.Kind.ToString(), s.Status.ToString(), s.CustomerPo,
            s.PromisedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), s.ShippedAt is { } sa ? Local(sa, "yyyy-MM-dd") : null, site.Name,
            new DocParty(site.Name, Split(site.Address)), shipTo,
            cust is null ? null : new DocCustomer(cust.Code, cust.Name, cust.ContactName, cust.Phone),
            car is null ? null : new DocCarrier(car.Code, car.Name, car.Scac, s.ServiceLevel, s.TrackingNumber), s.GrossWeight, s.FreightTerms, s.SpecialInstructions,
            d.Lines.Select(l => new DocLine(l.Line.LineNumber, l.PartNumber, l.PartName, l.Line.Revision, l.Line.Quantity, uoms.GetValueOrDefault(l.Line.PartId), l.LotNumber)).ToList(),
            d.Containers.OrderBy(c => c.Container.ParentId is null ? 0 : 1).ThenBy(c => c.Container.Number, StringComparer.Ordinal)
                .Select(c => new DocContainer(c.Container.Number, c.Container.Kind.ToString(), c.Container.ParentId is { } p ? byId.GetValueOrDefault(p) : null, c.Container.Weight,
                    c.Container.LengthCm, c.Container.WidthCm, c.Container.HeightCm, c.Items.Select(i => new DocItem(i.PartNumber, i.LotNumber, i.SerialNumber, i.Quantity)).ToList())).ToList(),
            [], Local(m.Now, "yyyy-MM-dd HH:mm"));
    }

    async Task<(Guid Id, byte[] Pdf)> IssueAsync(DocData data, Guid siteId, Guid? shipmentId, Guid? customerId, Guid? carrierId, CancellationToken ct)
    {
        var sel = await SelectAsync(data.Kind, siteId, customerId, carrierId, ct);
        DocLayout layout; byte[]? logo = null;
        if (sel is { } x) { layout = Approved(x.T, x.R); logo = x.R.Logo; } else layout = ShippingDocumentLayouts.BuiltIn(data.Kind);
        var (pdf, pages) = ShippingDocumentRenderer.Render(layout, data, logo);
        var g = new GeneratedShippingDocument { Kind = data.Kind, ShipmentId = shipmentId, SiteId = siteId, Reference = data.Reference, TemplateId = sel?.T.Id, TemplateRevisionId = sel?.R.Id,
            TemplateCode = sel?.T.Code, TemplateRevision = sel?.R.Revision, BuiltIn = sel is null, DataJson = JsonSerializer.Serialize(data, ShippingDocumentLayouts.Json), Pdf = pdf,
            PdfSha256 = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant(), Pages = pages, GeneratedAt = m.Now, GeneratedById = m.UserId };
        db.GeneratedShippingDocuments.Add(g);
        await db.SaveChangesAsync(ct);
        return (g.Id, pdf);
    }

    /// <summary>Issues a packing list or bill of lading from the template that applies now, and records it.</summary>
    public async Task<(Guid Id, byte[] Pdf)> GenerateAsync(Guid shipmentId, ShippingDocumentKind kind, CancellationToken ct)
    {
        if (kind == ShippingDocumentKind.ShippingManifest) throw new AppException("Manifests are generated per site, carrier and day.");
        var data = await ShipmentDataAsync(shipmentId, kind, preview: false, ct);
        var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == shipmentId, ct);
        return await IssueAsync(data, s.SiteId, s.Id, s.CustomerId, s.CarrierId, ct);
    }

    /// <summary>Manifest of everything shipped with one carrier from one site on one (site-local) day.</summary>
    public async Task<(Guid Id, byte[] Pdf)> ManifestAsync(Guid siteId, Guid carrierId, DateOnly date, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ShippingView, siteId, null, ct);
        var site = await db.Sites.AsNoTracking().FirstOrDefaultAsync(x => x.Id == siteId, ct) ?? throw new NotFoundException("Site");
        var car = await db.Carriers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == carrierId, ct) ?? throw new NotFoundException("Carrier");
        var zone = await m.ZoneAsync(siteId, ct);
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        var end = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        var ships = await db.Shipments.AsNoTracking().Where(x => x.SiteId == siteId && x.CarrierId == carrierId && x.ShippedAt >= start && x.ShippedAt < end && x.Status != ShipmentStatus.Cancelled)
            .OrderBy(x => x.ShippedAt).ThenBy(x => x.Number).ToListAsync(ct);
        if (ships.Count == 0) throw new ConflictException($"Nothing was shipped with {car.Name} from {site.Name} on {date:yyyy-MM-dd}.");
        var ids = ships.Select(x => x.Id).ToList();
        var units = await db.ShipmentContainers.AsNoTracking().Where(x => ids.Contains(x.ShipmentId) && x.ParentId == null).GroupBy(x => x.ShipmentId).ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
        var custIds = ships.Select(x => x.CustomerId).OfType<Guid>().Distinct().ToList();
        var custs = await db.Customers.AsNoTracking().Where(x => custIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var number = $"MAN-{site.Code}-{car.Code}-{date:yyyyMMdd}";
        var data = new DocData(ShippingDocumentKind.ShippingManifest, number, null, null, null, null, null, null, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), site.Name,
            new DocParty(site.Name, (site.Address ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()), new DocParty(null, []), null,
            new DocCarrier(car.Code, car.Name, car.Scac, null, null), null, null, null, [], [],
            ships.Select(x => new DocManifestRow(x.Number, x.CustomerId is { } c ? custs.GetValueOrDefault(c) : null,
                x.ShipToCity is not null ? string.Join(" ", new[] { x.ShipToCity, x.ShipToRegion }.Where(v => !string.IsNullOrWhiteSpace(v))) : x.ShipTo,
                x.TrackingNumber, x.BolNumber, units.GetValueOrDefault(x.Id), x.GrossWeight)).ToList(),
            TimeZoneInfo.ConvertTime(m.Now, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        return await IssueAsync(data, siteId, null, null, carrierId, ct);
    }

    async Task<GeneratedShippingDocument> IssuedAsync(Guid id, CancellationToken ct)
    {
        var g = await db.GeneratedShippingDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Document");
        if (g.ShipmentId is { } sid) await m.ViewAsync(ShippingService.ShipmentType, sid, ct);
        else if (!await m.CanAsync(Permissions.ShippingView, g.SiteId, null, ct)) throw new NotFoundException("Document");
        return g;
    }

    public async Task<List<GeneratedRow>> IssuedListAsync(Guid? shipmentId, Guid? siteId, ShippingDocumentKind? kind, CancellationToken ct)
    {
        IQueryable<GeneratedShippingDocument> q = db.GeneratedShippingDocuments.AsNoTracking();
        if (shipmentId is { } sid) { await m.ViewAsync(ShippingService.ShipmentType, sid, ct); q = q.Where(x => x.ShipmentId == sid); }
        else if (siteId is { } site) { await m.RequireAsync(Permissions.ShippingView, site, null, ct); q = q.Where(x => x.SiteId == site); }
        else throw new AppException("Choose a shipment or a site.");
        if (kind is { } k) q = q.Where(x => x.Kind == k);
        var rows = await q.OrderByDescending(x => x.GeneratedAt).Take(200)
            .Select(x => new { x.Id, x.Kind, x.Reference, x.TemplateCode, x.TemplateRevision, x.BuiltIn, x.Pages, x.PdfSha256, x.GeneratedAt, x.GeneratedById }).ToListAsync(ct);
        var uids = rows.Select(r => r.GeneratedById).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return rows.Select(r => new GeneratedRow(r.Id, r.Kind, r.Reference, r.TemplateCode, r.TemplateRevision, r.BuiltIn, r.Pages, r.PdfSha256, r.GeneratedAt, users.GetValueOrDefault(r.GeneratedById))).ToList();
    }

    public async Task<(byte[] Pdf, string Name)> IssuedPdfAsync(Guid id, CancellationToken ct)
    {
        var g = await IssuedAsync(id, ct);
        return (g.Pdf, $"{g.Reference}-{g.Kind}.pdf");
    }

    /// <summary>Renders the stored snapshot again with the revision that was used and compares the bytes with the issued PDF.</summary>
    public async Task<ReproduceResult> ReproduceAsync(Guid id, CancellationToken ct)
    {
        var g = await IssuedAsync(id, ct);
        var data = JsonSerializer.Deserialize<DocData>(g.DataJson, ShippingDocumentLayouts.Json)!;
        DocLayout layout; byte[]? logo = null;
        if (g.TemplateRevisionId is { } rid)
        {
            var rev = await db.ShippingDocumentTemplateRevisions.AsNoTracking().FirstAsync(x => x.Id == rid, ct);
            var t = await db.ShippingDocumentTemplates.AsNoTracking().FirstAsync(x => x.Id == rev.TemplateId, ct);
            layout = Approved(t, rev); logo = rev.Logo;
        }
        else layout = ShippingDocumentLayouts.BuiltIn(g.Kind);
        var pdf = ShippingDocumentRenderer.Render(layout, data, logo).Pdf;
        var sha = Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant();
        return new(sha == g.PdfSha256, g.PdfSha256, sha, g.TemplateCode, g.TemplateRevision, g.BuiltIn);
    }

    public async Task SaveTermsAsync(Guid shipmentId, ShippingTermsInput r, CancellationToken ct)
    {
        await m.EditAsync(ShippingService.ShipmentType, shipmentId, ct);
        var s = await db.Shipments.FirstAsync(x => x.Id == shipmentId, ct);
        if (s.Status is ShipmentStatus.Shipped or ShipmentStatus.Delivered or ShipmentStatus.Cancelled) throw new ConflictException($"Not allowed while the shipment is {s.Status}; issued documents keep what they printed.");
        if (r.FreightTerms?.Length > 60) throw new AppException("Freight terms are longer than 60 characters.");
        if (r.SpecialInstructions?.Length > 2000) throw new AppException("Special instructions are longer than 2,000 characters.");
        s.FreightTerms = string.IsNullOrWhiteSpace(r.FreightTerms) ? null : r.FreightTerms.Trim();
        s.SpecialInstructions = string.IsNullOrWhiteSpace(r.SpecialInstructions) ? null : r.SpecialInstructions.Trim();
        await db.SaveChangesAsync(ct);
    }
}
