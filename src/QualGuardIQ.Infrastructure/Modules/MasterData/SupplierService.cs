using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.MasterData;

public sealed record SaveSupplierRequest(string Code, string LegalName, string? OperatingName, string Category, string? SuppliedScope, Criticality Criticality,
    Criticality Risk, string? Address, string? Website, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, uint RowVersion = 0);
public sealed record SupplierApprovalRequest(SupplierStatus ProposedStatus, string Scope, DateOnly? NextReviewOn);
public sealed record SupplierStatusRequest(string Reason);
public sealed record SaveContactRequest(string Name, string? Role, string? Email, string? Phone);
public sealed record SaveCertificationRequest(string Standard, string? CertificateNumber, string? Issuer, DateOnly? IssuedOn, DateOnly ExpiresOn);
public sealed record SupplierCondition(string Condition, SupplierRestriction Restriction, string Message);
public sealed record SupplierCheck(bool Blocked, IReadOnlyList<SupplierCondition> Conditions);

/// <summary>Supplier master, qualification and approval status, certifications, and the configurable restriction policy.</summary>
public sealed class SupplierService(AppDbContext db, ModuleSupport m, WorkflowService workflow, INotifier notifier)
{
    public const string RecordType = "Supplier";

    public async Task<Supplier> SaveAsync(Guid? id, SaveSupplierRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.SuppliersEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Code, "Supplier code", 40);
        ModuleSupport.Required(r.LegalName, "Legal name", 200);
        ModuleSupport.Required(r.Category, "Category", 100);
        var code = r.Code.Trim();
        if (await db.Suppliers.AnyAsync(s => s.Code == code && s.Id != id, ct)) throw new ConflictException("A supplier with this code already exists.");
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        Supplier s;
        if (id is null) { s = new Supplier { Status = SupplierStatus.Prospective }; db.Suppliers.Add(s); }
        else
        {
            await m.EditAsync(RecordType, id.Value, ct);
            s = await db.Suppliers.FirstAsync(x => x.Id == id, ct);
            db.Entry(s).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        }
        s.Code = code; s.LegalName = r.LegalName.Trim(); s.OperatingName = r.OperatingName?.Trim(); s.Category = r.Category.Trim();
        s.SuppliedScope = r.SuppliedScope?.Trim(); s.Criticality = r.Criticality; s.Risk = r.Risk; s.Address = r.Address?.Trim(); s.Website = r.Website?.Trim();
        s.SiteId = r.SiteId; s.DepartmentId = r.DepartmentId; s.OwnerId = owner;
        await db.SaveChangesAsync(ct);
        await IndexAsync(s, ct);
        return s;
    }

    /// <summary>Qualification outcome goes through the shared approval engine (e-signature). Approval sets the proposed status and scope.</summary>
    public async Task RequestApprovalAsync(Guid id, SupplierApprovalRequest r, CancellationToken ct)
    {
        var s = await LoadEditAsync(id, ct);
        if (r.ProposedStatus is not (SupplierStatus.Approved or SupplierStatus.Conditional)) throw new AppException("Propose Approved or Conditional.");
        if (s.Status is SupplierStatus.PendingApproval) throw new ConflictException("This supplier is already waiting for approval.");
        ModuleSupport.Required(r.Scope, "Approved scope", 2000);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        s.ProposedStatus = r.ProposedStatus;
        s.ProposedScope = r.Scope.Trim();
        s.NextReviewOn = r.NextReviewOn ?? s.NextReviewOn;
        s.StatusReason = $"Previous status: {s.Status}";
        s.Status = SupplierStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Suppliers.FirstAsync(x => x.Id == id, ct);
        s.Status = s.ProposedStatus ?? SupplierStatus.Approved;
        s.ApprovedScope = s.ProposedScope;
        s.ApprovedOn = await m.TodayAsync(s.SiteId, ct);
        s.NextReviewOn ??= s.ApprovedOn.Value.AddYears(1);
        s.ProposedStatus = null; s.ProposedScope = null; s.StatusReason = null;
        await db.SaveChangesAsync(ct);
        await IndexAsync(s, ct);
        await notifier.NotifyAsync([s.OwnerId], $"Supplier {s.Code} is {s.Status}", s.ApprovedScope, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var s = await db.Suppliers.FirstAsync(x => x.Id == id, ct);
        var prev = s.StatusReason?.Replace("Previous status: ", "");
        s.Status = Enum.TryParse<SupplierStatus>(prev, out var p) && p != SupplierStatus.PendingApproval ? p : SupplierStatus.Prospective;
        s.StatusReason = comment;
        s.ProposedStatus = null; s.ProposedScope = null;
        await db.SaveChangesAsync(ct);
        await IndexAsync(s, ct);
    }

    public Task SuspendAsync(Guid id, string reason, CancellationToken ct) => ChangeStatusAsync(id, SupplierStatus.Suspended, reason, ct);
    public Task DisqualifyAsync(Guid id, string reason, CancellationToken ct) => ChangeStatusAsync(id, SupplierStatus.Disqualified, reason, ct);

    /// <summary>Suspension and disqualification are decided by a supplier approver and always need a reason.</summary>
    private async Task ChangeStatusAsync(Guid id, SupplierStatus to, string reason, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var s = await db.Suppliers.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.SuppliersApprove, s.SiteId, s.DepartmentId, ct);
        ModuleSupport.Required(reason, "Reason", 2000);
        if (s.Status == to) throw new ConflictException($"The supplier is already {to}.");
        if (s.Status == SupplierStatus.PendingApproval) throw new ConflictException("Finish the pending approval first.");
        s.Status = to;
        s.StatusReason = reason.Trim();
        await db.SaveChangesAsync(ct);
        await IndexAsync(s, ct);
        await notifier.NotifyAsync([s.OwnerId], $"Supplier {s.Code} {to.ToString().ToLowerInvariant()}", reason, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task<SupplierContact> AddContactAsync(Guid id, SaveContactRequest r, CancellationToken ct)
    {
        await LoadEditAsync(id, ct);
        ModuleSupport.Required(r.Name, "Name", 200);
        if (!string.IsNullOrWhiteSpace(r.Email) && !System.Net.Mail.MailAddress.TryCreate(r.Email, out _)) throw new AppException("Enter a valid email address.");
        var c = new SupplierContact { SupplierId = id, Name = r.Name.Trim(), Role = r.Role?.Trim(), Email = r.Email?.Trim(), Phone = r.Phone?.Trim() };
        db.SupplierContacts.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task RemoveContactAsync(Guid id, Guid contactId, CancellationToken ct)
    {
        await LoadEditAsync(id, ct);
        var c = await db.SupplierContacts.FirstOrDefaultAsync(x => x.Id == contactId && x.SupplierId == id, ct) ?? throw new NotFoundException("Contact");
        db.SupplierContacts.Remove(c);
        await db.SaveChangesAsync(ct);
    }

    public async Task<SupplierCertification> AddCertificationAsync(Guid id, SaveCertificationRequest r, CancellationToken ct)
    {
        await LoadEditAsync(id, ct);
        ModuleSupport.Required(r.Standard, "Standard", 100);
        if (r.IssuedOn is { } i && r.ExpiresOn < i) throw new AppException("The expiration date must be after the issue date.");
        var c = new SupplierCertification { SupplierId = id, Standard = r.Standard.Trim(), CertificateNumber = r.CertificateNumber?.Trim(), Issuer = r.Issuer?.Trim(), IssuedOn = r.IssuedOn, ExpiresOn = r.ExpiresOn };
        db.SupplierCertifications.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    /// <summary>Evaluates the organization's restriction policy for this supplier today.</summary>
    public async Task<SupplierCheck> CheckAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Supplier");
        var org = await db.OrganizationSettings.AsNoTracking().FirstAsync(ct);
        var today = await m.TodayAsync(s.SiteId, ct);
        var list = new List<SupplierCondition>();
        void Add(string cond, SupplierRestriction r, string msg) { if (r != SupplierRestriction.None) list.Add(new(cond, r, msg)); }
        if (s.Status == SupplierStatus.Suspended) Add("Suspended", org.SupplierSuspendedPolicy, $"{s.Code} is suspended: {s.StatusReason}");
        else if (s.Status == SupplierStatus.Disqualified) Add("Disqualified", org.SupplierDisqualifiedPolicy, $"{s.Code} is disqualified: {s.StatusReason}");
        else if (s.Status is SupplierStatus.Prospective or SupplierStatus.PendingApproval) Add("Unapproved", org.SupplierUnapprovedPolicy, $"{s.Code} is not approved.");
        var expired = await db.SupplierCertifications.Where(c => c.SupplierId == id && c.ExpiresOn < today).Select(c => c.Standard).ToListAsync(ct);
        var current = await db.SupplierCertifications.Where(c => c.SupplierId == id && c.ExpiresOn >= today).Select(c => c.Standard).ToListAsync(ct);
        var lapsed = expired.Except(current).Distinct().ToList();
        if (lapsed.Count > 0) Add("CertificationExpired", org.SupplierExpiredCertPolicy, $"Expired certification: {string.Join(", ", lapsed)}");
        return new SupplierCheck(list.Any(c => c.Restriction == SupplierRestriction.Block), list);
    }

    /// <summary>
    /// Used by modules before accepting a supplier on a record. Warnings pass; blocks need the override permission
    /// and a reason, and every override is recorded with the user, time, record and condition.
    /// </summary>
    public async Task<SupplierCheck> EnforceAsync(Guid supplierId, string recordType, Guid? recordId, string? overrideReason, CancellationToken ct)
    {
        var check = await CheckAsync(supplierId, ct);
        if (!check.Blocked) return check;
        var s = await db.Suppliers.AsNoTracking().FirstAsync(x => x.Id == supplierId, ct);
        if (string.IsNullOrWhiteSpace(overrideReason))
            throw new AppException("This supplier is restricted: " + string.Join("; ", check.Conditions.Where(c => c.Restriction == SupplierRestriction.Block).Select(c => c.Message)));
        if (!await m.CanAsync(Permissions.SuppliersOverride, s.SiteId, s.DepartmentId, ct)) throw new ForbiddenException("You are not allowed to override supplier restrictions.");
        foreach (var c in check.Conditions.Where(c => c.Restriction == SupplierRestriction.Block))
            db.SupplierOverrides.Add(new SupplierOverride { SupplierId = supplierId, RecordType = recordType, RecordId = recordId, Condition = c.Condition, Reason = overrideReason.Trim(), UserId = m.UserId });
        await db.SaveChangesAsync(ct);
        return check;
    }

    private async Task<Supplier> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Suppliers.FirstAsync(x => x.Id == id, ct);
    }

    private Task IndexAsync(Supplier s, CancellationToken ct) => m.IndexAsync(RecordType, s.Id, $"{s.OperatingName} {s.Category} {s.SuppliedScope} {s.ApprovedScope}", ct);
}
