using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Apqp;

/// <summary>MustChangePassword (default true): the password given here is temporary and the first sign-in asks for a new one.</summary>
public sealed record PortalAccountInput(string Email, string DisplayName, string Kind, Guid PartyId, string Password, bool? MustChangePassword = null);
public sealed record PortalAccountDto(Guid Id, string Email, string DisplayName, string Kind, Guid PartyId, string PartyName, bool IsActive, DateTimeOffset? LastSignInAt);
public sealed record PortalMe(Guid UserId, string DisplayName, string Kind, Guid PartyId, string PartyName);
public sealed record PortalResponseInput(string Body);
public sealed record PortalScarResponseInput(string Containment, string RootCause, string CorrectiveActionPlan);
public sealed record PortalEvidence(Guid Id, string FileName, long SizeBytes, DateTimeOffset UploadedAt, bool Mine);

/// <summary>
/// External customer and supplier portals. Portal accounts hold no staff roles; every query here is filtered by the account's party
/// (customer or supplier) and returns a reduced field set. Records of other parties answer 404, never 403, so their existence is not revealed.
/// </summary>
public sealed class PortalService(AppDbContext db, ICurrentUser current, UserManager<ApplicationUser> users, IAccessService access, IClock clock, INotifier notifier,
    IRecordRegistry records, AttachmentService attachments, SupplierDevelopmentService suppliers, ISiteCalendar calendar)
{
    public const string Customer = "Customer", Supplier = "Supplier";

    Guid UserId => current.UserId ?? throw new ForbiddenException();

    public async Task<PortalMe> MeAsync(string? kind, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == UserId, ct) ?? throw new ForbiddenException();
        if (u.PortalKind is null || u.PortalPartyId is null || !u.IsActive) throw new ForbiddenException("This area is for customer and supplier portal accounts.");
        if (kind is not null && u.PortalKind != kind) throw new ForbiddenException($"This area is for {kind.ToLowerInvariant()} portal accounts.");
        var name = u.PortalKind == Customer
            ? await db.Customers.AsNoTracking().Where(c => c.Id == u.PortalPartyId).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : await db.Suppliers.AsNoTracking().Where(s => s.Id == u.PortalPartyId).Select(s => s.LegalName).FirstOrDefaultAsync(ct);
        return new PortalMe(u.Id, u.DisplayName, u.PortalKind, u.PortalPartyId.Value, name ?? "");
    }

    // ---------- administration (staff) ----------
    async Task RequireAdminAsync(CancellationToken ct)
    {
        if ((await access.GetScopeAsync(UserId, Permissions.PortalAdmin, ct)).IsEmpty) throw new ForbiddenException();
    }

    public async Task<Guid> CreateAccountAsync(PortalAccountInput r, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        ModuleSupport.Required(r.Email, "Email", 256);
        ModuleSupport.Required(r.DisplayName, "Name", 200);
        if (r.Kind is not (Customer or Supplier)) throw new AppException("Portal kind is Customer or Supplier.");
        var exists = r.Kind == Customer ? await db.Customers.AnyAsync(c => c.Id == r.PartyId, ct) : await db.Suppliers.AnyAsync(s => s.Id == r.PartyId, ct);
        if (!exists) throw new AppException($"Select an existing {r.Kind.ToLowerInvariant()}.");
        var u = new ApplicationUser { UserName = r.Email.Trim(), Email = r.Email.Trim(), EmailConfirmed = true, DisplayName = r.DisplayName.Trim(), CreatedAt = clock.UtcNow, PortalKind = r.Kind, PortalPartyId = r.PartyId, MustChangePassword = r.MustChangePassword ?? true };
        var res = await users.CreateAsync(u, r.Password);
        if (!res.Succeeded) throw new AppException(string.Join(" ", res.Errors.Select(e => e.Description)));
        return u.Id;
    }

    public async Task<List<PortalAccountDto>> AccountsAsync(CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        var list = await db.Users.AsNoTracking().Where(u => u.PortalKind != null).OrderBy(u => u.DisplayName).ToListAsync(ct);
        var cust = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var sup = await db.Suppliers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.LegalName, ct);
        return list.Select(u => new PortalAccountDto(u.Id, u.Email!, u.DisplayName, u.PortalKind!, u.PortalPartyId!.Value,
            (u.PortalKind == Customer ? cust : sup).GetValueOrDefault(u.PortalPartyId.Value, ""), u.IsActive, u.LastSignInAt)).ToList();
    }

    public async Task SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        await RequireAdminAsync(ct);
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == id && x.PortalKind != null, ct) ?? throw new NotFoundException("Portal account");
        u.IsActive = active;
        await db.SaveChangesAsync(ct);
        await users.UpdateSecurityStampAsync(u);
    }

    // ---------- customer portal ----------
    public async Task<List<object>> ComplaintsAsync(CancellationToken ct)
    {
        var me = await MeAsync(Customer, ct);
        return (await db.Complaints.AsNoTracking().Where(c => c.CustomerId == me.PartyId).OrderByDescending(c => c.ReportedOn)
            .Select(c => new { c.Id, c.Number, c.Title, c.CustomerReference, c.ReportedOn, c.ResponseDueOn, Status = c.Status.ToString(), Responded = c.ResponseSentOn != null }).Take(500).ToListAsync(ct)).Cast<object>().ToList();
    }

    async Task<Complaint> OwnComplaintAsync(Guid id, Guid party, CancellationToken ct) =>
        await db.Complaints.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id && c.CustomerId == party, ct) ?? throw new NotFoundException("Complaint");

    public async Task<object> ComplaintAsync(Guid id, CancellationToken ct)
    {
        var me = await MeAsync(Customer, ct);
        var c = await OwnComplaintAsync(id, me.PartyId, ct);
        // Corrective actions are shown by status and summary only once the response has been sent.
        var capaIds = await db.RecordLinks.AsNoTracking().Where(l => l.FromType == "Complaint" && l.FromId == id && l.ToType == "Capa").Select(l => l.ToId)
            .ToListAsync(ct);
        var capas = c.ResponseSentOn is null ? [] : await db.Capas.AsNoTracking().Where(x => capaIds.Contains(x.Id))
            .Select(x => new { x.Number, x.Title, Status = x.Status.ToString(), x.ClosedAt }).ToListAsync(ct);
        return new
        {
            c.Id, c.Number, c.Title, c.CustomerReference, c.PartNumber, c.LotNumber, c.ReportedOn, c.ResponseDueOn, Status = c.Status.ToString(),
            Response = c.ResponseSentOn is null ? null : c.CustomerResponse, c.ResponseSentOn, RootCause = c.ResponseSentOn is null ? null : c.RootCause,
            CorrectiveActions = capas, Messages = await MessagesAsync("Complaint", id, ct), Evidence = await EvidenceAsync("Complaint", id, me, ct)
        };
    }

    public async Task<object> QualityReportAsync(CancellationToken ct)
    {
        var me = await MeAsync(Customer, ct);
        var since = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddMonths(-12);
        var comp = await db.Complaints.AsNoTracking().Where(c => c.CustomerId == me.PartyId && c.ReportedOn >= since).Select(c => new { c.ReportedOn, c.Status }).ToListAsync(ct);
        var fai = await db.Fais.AsNoTracking().Where(f => f.CustomerId == me.PartyId).GroupBy(f => f.Status).Select(g => new { Status = g.Key.ToString(), Count = g.Count() }).ToListAsync(ct);
        var sinceTs = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var aql = await db.AqlInspections.AsNoTracking().Where(a => a.CustomerId == me.PartyId && a.OverallResult != null && a.InspectionDate >= sinceTs).Select(a => a.OverallResult).ToListAsync(ct);
        return new
        {
            Customer = me.PartyName, PeriodFrom = since,
            ComplaintsByMonth = comp.GroupBy(c => c.ReportedOn.ToString("yyyy-MM")).OrderBy(g => g.Key).Select(g => new { Month = g.Key, Count = g.Count() }),
            OpenComplaints = comp.Count(c => c.Status is not (ComplaintStatus.Closed or ComplaintStatus.Cancelled)),
            FirstArticles = fai,
            OutgoingLots = aql.Count, LotsAccepted = aql.Count(x => x == AqlDecision.Accept)
        };
    }

    // ---------- supplier portal ----------
    public async Task<List<object>> NcrsAsync(CancellationToken ct)
    {
        var me = await MeAsync(Supplier, ct);
        return (await db.Ncrs.AsNoTracking().Where(n => n.SupplierId == me.PartyId).OrderByDescending(n => n.DetectedOn)
            .Select(n => new { n.Id, n.Number, n.Title, n.PartNumber, n.LotNumber, n.QuantityAffected, n.DetectedOn, Status = n.Status.ToString() }).Take(500).ToListAsync(ct)).Cast<object>().ToList();
    }

    public async Task<object> NcrAsync(Guid id, CancellationToken ct)
    {
        var me = await MeAsync(Supplier, ct);
        var n = await db.Ncrs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.SupplierId == me.PartyId, ct) ?? throw new NotFoundException("NCR");
        return new { n.Id, n.Number, n.Title, n.Description, n.PartNumber, n.LotNumber, n.QuantityAffected, n.DetectedOn, Status = n.Status.ToString(),
            Messages = await MessagesAsync("Ncr", id, ct), Evidence = await EvidenceAsync("Ncr", id, me, ct) };
    }

    public async Task<List<object>> ScarsAsync(CancellationToken ct)
    {
        var me = await MeAsync(Supplier, ct);
        return (await db.SupplierCorrectiveActions.AsNoTracking().Where(s => s.SupplierId == me.PartyId).OrderByDescending(s => s.IssuedOn)
            .Select(s => new { s.Id, s.Number, s.Title, s.IssuedOn, s.ResponseDueOn, Status = s.Status.ToString() }).Take(500).ToListAsync(ct)).Cast<object>().ToList();
    }

    public async Task<object> ScarAsync(Guid id, CancellationToken ct)
    {
        var me = await MeAsync(Supplier, ct);
        var s = await db.SupplierCorrectiveActions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.SupplierId == me.PartyId, ct) ?? throw new NotFoundException("SCAR");
        return new { s.Id, s.Number, s.Title, s.Problem, s.IssuedOn, s.ResponseDueOn, Status = s.Status.ToString(), s.SupplierContainment, s.SupplierRootCause, s.CorrectiveActionPlan,
            s.ResponseReceivedOn, s.ResponseRejections, ReviewNotes = s.ResponseRejections > 0 ? s.ReviewNotes : null, s.WasEffective,
            Messages = await MessagesAsync("SupplierCorrectiveAction", id, ct), Evidence = await EvidenceAsync("SupplierCorrectiveAction", id, me, ct) };
    }

    /// <summary>The supplier answers an issued SCAR (or one whose previous answer was rejected); the SCAR owner is notified to review it.</summary>
    public async Task RespondScarAsync(Guid id, PortalScarResponseInput r, CancellationToken ct)
    {
        var me = await MeAsync(Supplier, ct);
        var s = await db.SupplierCorrectiveActions.FirstOrDefaultAsync(x => x.Id == id && x.SupplierId == me.PartyId, ct) ?? throw new NotFoundException("SCAR");
        if (s.Status != ScarStatus.Issued) throw new ConflictException("This SCAR is not waiting for your response.");
        ModuleSupport.Required(r.Containment, "Containment", 4000);
        ModuleSupport.Required(r.RootCause, "Root cause", 4000);
        ModuleSupport.Required(r.CorrectiveActionPlan, "Corrective action plan", 4000);
        s.SupplierContainment = r.Containment.Trim(); s.SupplierRootCause = r.RootCause.Trim(); s.CorrectiveActionPlan = r.CorrectiveActionPlan.Trim();
        s.SupplierRespondent = $"{me.DisplayName} (supplier portal)"; s.ResponseReceivedOn = await calendar.TodayAsync(s.SiteId, ct); s.Status = ScarStatus.ResponseReceived;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([s.OwnerId], $"SCAR {s.Number}: supplier responded", $"{me.PartyName} submitted containment, root cause and corrective action.", await records.GetAsync("SupplierCorrectiveAction", s.Id, ct), ct: ct);
    }

    public async Task<object> ScorecardAsync(CancellationToken ct)
    {
        var me = await MeAsync(Supplier, ct);
        var row = (await suppliers.ScorecardAsync(null, null, me.PartyId, ct, unscoped: true)).FirstOrDefault();
        var perf = await db.SupplierPerformance.AsNoTracking().Where(p => p.SupplierId == me.PartyId).OrderByDescending(p => p.PeriodStart).Take(12)
            .Select(p => new { p.PeriodStart, p.PeriodEnd, p.LotsReceived, p.LotsRejected, p.Deliveries, p.OnTimeDeliveries, p.QualityScore, p.DeliveryScore, p.ResponsivenessScore }).ToListAsync(ct);
        var aql = await db.AqlInspections.AsNoTracking().Where(a => a.SupplierId == me.PartyId && a.OverallResult != null).OrderByDescending(a => a.InspectionDate).Take(50)
            .Select(a => new { Number = a.InspectionNumber, a.InspectionDate, a.LotSize, a.SampleSize, Result = a.OverallResult!.Value.ToString() }).ToListAsync(ct);
        return new { Scorecard = row is null ? null : row with { Name = me.PartyName }, Performance = perf, RecentLots = aql };
    }

    public async Task<List<object>> AuditsAsync(CancellationToken ct)
    {
        var me = await MeAsync(Supplier, ct);
        var ids = await db.RecordLinks.AsNoTracking().Where(l => (l.FromType == "Supplier" && l.FromId == me.PartyId && l.ToType == "Audit") || (l.ToType == "Supplier" && l.ToId == me.PartyId && l.FromType == "Audit"))
            .Select(l => l.FromType == "Audit" ? l.FromId : l.ToId).ToListAsync(ct);
        var audits = await db.Audits.AsNoTracking().Where(a => ids.Contains(a.Id) && (a.Status == AuditStatus.Reporting || a.Status == AuditStatus.Closed))
            .Select(a => new { a.Id, a.Number, a.Title, a.PlannedStart, a.PlannedEnd, Status = a.Status.ToString(), a.Summary }).ToListAsync(ct);
        var aid = audits.Select(a => a.Id).ToList();
        var findings = await db.AuditFindings.AsNoTracking().Where(f => aid.Contains(f.AuditId)).Select(f => new { f.AuditId, f.Number, Type = f.FindingType.ToString(), f.Statement, f.ResponseDueOn, Status = f.Status.ToString() }).ToListAsync(ct);
        return audits.Select(a => (object)new { a.Id, a.Number, a.Title, a.PlannedStart, a.PlannedEnd, a.Status, a.Summary, Findings = findings.Where(f => f.AuditId == a.Id) }).ToList();
    }

    // ---------- shared: messages and evidence ----------
    async Task CheckOwnAsync(string type, Guid id, PortalMe me, CancellationToken ct)
    {
        var ok = (me.Kind, type) switch
        {
            (Customer, "Complaint") => await db.Complaints.AnyAsync(c => c.Id == id && c.CustomerId == me.PartyId, ct),
            (Supplier, "Ncr") => await db.Ncrs.AnyAsync(n => n.Id == id && n.SupplierId == me.PartyId, ct),
            (Supplier, "SupplierCorrectiveAction") => await db.SupplierCorrectiveActions.AnyAsync(s => s.Id == id && s.SupplierId == me.PartyId, ct),
            _ => false
        };
        if (!ok) throw new NotFoundException("Record");
    }

    static string Type(string kind) => kind switch { "complaints" => "Complaint", "ncrs" => "Ncr", "scars" => "SupplierCorrectiveAction", _ => throw new NotFoundException("Record") };

    async Task<List<object>> MessagesAsync(string type, Guid id, CancellationToken ct) =>
        (await db.PortalResponses.AsNoTracking().Where(r => r.RecordType == type && r.RecordId == id).OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Body, r.CreatedAt, Author = db.Users.Where(u => u.Id == r.AuthorId).Select(u => u.DisplayName).First() }).ToListAsync(ct)).Cast<object>().ToList();

    async Task<List<PortalEvidence>> EvidenceAsync(string type, Guid id, PortalMe me, CancellationToken ct)
    {
        var partyUsers = await db.Users.AsNoTracking().Where(u => u.PortalKind == me.Kind && u.PortalPartyId == me.PartyId).Select(u => u.Id).ToListAsync(ct);
        return await db.Attachments.AsNoTracking().Where(a => a.OwnerType == type && a.OwnerId == id && !a.IsArchived && a.CreatedById != null && partyUsers.Contains(a.CreatedById.Value))
            .OrderBy(a => a.CreatedAt).Select(a => new PortalEvidence(a.Id, a.FileName, a.SizeBytes, a.CreatedAt, a.CreatedById == me.UserId)).ToListAsync(ct);
    }

    public async Task RespondAsync(string kind, Guid id, PortalResponseInput r, CancellationToken ct)
    {
        var me = await MeAsync(null, ct);
        var type = Type(kind);
        await CheckOwnAsync(type, id, me, ct);
        ModuleSupport.Required(r.Body, "Response", 10000);
        db.PortalResponses.Add(new PortalResponse { RecordType = type, RecordId = id, PartyKind = me.Kind, PartyId = me.PartyId, AuthorId = me.UserId, Body = r.Body.Trim() });
        await db.SaveChangesAsync(ct);
        var rec = await records.GetAsync(type, id, ct);
        var owners = await access.UsersWithPermissionAsync(rec.EditPermission, rec.SiteId, rec.DepartmentId, ct);
        await notifier.NotifyAsync(owners, $"{rec.Number}: response from {me.PartyName}", r.Body.Length > 300 ? r.Body[..300] + "…" : r.Body, rec, ct: ct);
    }

    public async Task<PortalEvidence> UploadEvidenceAsync(string kind, Guid id, string fileName, string contentType, Stream content, CancellationToken ct)
    {
        var me = await MeAsync(null, ct);
        var type = Type(kind);
        await CheckOwnAsync(type, id, me, ct);
        var a = await attachments.UploadAsync(type, id, new UploadRequest(fileName, contentType, content, "Portal evidence", $"Uploaded by {me.PartyName} through the portal", Classification.Confidential, null), ct, skipPermission: true);
        return new PortalEvidence(a.Id, a.FileName, a.SizeBytes, a.CreatedAt, true);
    }

    /// <summary>Returns the attachment id if this portal account's party uploaded it on a record the party owns.</summary>
    public async Task<Domain.Shared.Attachment> EvidenceFileAsync(Guid attachmentId, CancellationToken ct)
    {
        var me = await MeAsync(null, ct);
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId && !x.IsArchived, ct) ?? throw new NotFoundException("File");
        await CheckOwnAsync(a.OwnerType, a.OwnerId, me, ct);
        var partyUsers = await db.Users.AsNoTracking().Where(u => u.PortalKind == me.Kind && u.PortalPartyId == me.PartyId).Select(u => u.Id).ToListAsync(ct);
        if (a.CreatedById is null || !partyUsers.Contains(a.CreatedById.Value)) throw new NotFoundException("File");
        return a;
    }
}
