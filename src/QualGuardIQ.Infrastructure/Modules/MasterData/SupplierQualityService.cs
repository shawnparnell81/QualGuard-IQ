using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.MasterData;

public sealed record IssueScarRequest(Guid SupplierId, Guid? NcrId, string Title, string Problem, DateOnly ResponseDueOn, Guid? OwnerId, string? SourceType = null, Guid? SourceId = null);
public sealed record ScarResponseRequest(string Containment, string RootCause, string CorrectiveActionPlan, string Respondent, DateOnly ReceivedOn);
public sealed record ScarReviewRequest(bool Accept, string Notes);
public sealed record ScarVerifyRequest(bool Effective, string Notes);
public sealed record PerformanceRequest(DateOnly PeriodStart, DateOnly PeriodEnd, int LotsReceived, int LotsRejected, int Deliveries, int OnTimeDeliveries, string? Comments);
public sealed record SupplierReviewRequest(ReviewOutcome Outcome, string Summary, DateOnly NextReviewOn);

/// <summary>
/// Documented scorecard calculation (percentages, one decimal):
///   Quality = 100 x (1 - lots rejected / lots received); 100 when nothing was received.
///   Delivery = 100 x on-time deliveries / deliveries; 100 when nothing was delivered.
///   Responsiveness = 100 x SCARs answered by their due date / SCARs due in the period; 100 when none were due.
///   Overall = 0.5 x Quality + 0.3 x Delivery + 0.2 x Responsiveness.
/// </summary>
public static class SupplierScoring
{
    public const decimal QualityWeight = 0.5m, DeliveryWeight = 0.3m, ResponsivenessWeight = 0.2m;
    public static decimal Ratio(int good, int total) => total <= 0 ? 100m : Math.Round(100m * good / total, 1, MidpointRounding.AwayFromZero);
    public static (decimal Quality, decimal Delivery, decimal Responsiveness, decimal Overall) Score(int lots, int rejected, int deliveries, int onTime, int scarsDue, int scarsOnTime)
    {
        var q = Ratio(lots - rejected, lots);
        var d = Ratio(onTime, deliveries);
        var r = Ratio(scarsOnTime, scarsDue);
        return (q, d, r, Math.Round(q * QualityWeight + d * DeliveryWeight + r * ResponsivenessWeight, 1, MidpointRounding.AwayFromZero));
    }
}

/// <summary>Supplier corrective actions, performance scorecards and periodic reviews.</summary>
public sealed class SupplierQualityService(AppDbContext db, ModuleSupport m, INumberingService numbers, INotifier notifier, SupplierService suppliers)
{
    public const string ScarType = "SupplierCorrectiveAction";
    public static readonly string[] ScarSources = ["AqlInspection", "Audit", "Complaint"];

    public async Task<SupplierCorrectiveAction> IssueScarAsync(IssueScarRequest r, CancellationToken ct)
    {
        await m.ViewAsync(SupplierService.RecordType, r.SupplierId, ct);
        var s = await db.Suppliers.AsNoTracking().FirstAsync(x => x.Id == r.SupplierId, ct);
        await m.RequireAsync(Permissions.SuppliersEdit, s.SiteId, s.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Problem, "Problem description", 10000);
        var today = await m.TodayAsync(s.SiteId, ct);
        if (r.ResponseDueOn < today) throw new AppException("The response due date cannot be in the past.");
        if (r.NcrId is { } ncrId)
        {
            await m.ViewAsync(NcrService.RecordType, ncrId, ct);
            if (!await db.Ncrs.AnyAsync(n => n.Id == ncrId && n.SupplierId == r.SupplierId, ct)) throw new AppException("The NCR is not recorded against this supplier.");
        }
        if ((r.SourceType is null) != (r.SourceId is null)) throw new AppException("Give both the source type and the source record.");
        if (r.SourceType is { } st)
        {
            if (!ScarSources.Contains(st)) throw new AppException($"A SCAR can be raised from: {string.Join(", ", ScarSources)}.");
            await m.ViewAsync(st, r.SourceId!.Value, ct);
            if (st == "AqlInspection" && !await db.AqlInspections.AnyAsync(a => a.Id == r.SourceId && a.SupplierId == r.SupplierId, ct)) throw new AppException("The AQL lot is not from this supplier.");
        }
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new SupplierCorrectiveAction
        {
            Number = await numbers.NextAsync(ScarType, s.SiteId, ct), SupplierId = s.Id, NcrId = r.NcrId, SourceType = r.SourceType, SourceId = r.SourceId, Title = r.Title.Trim(), Problem = r.Problem.Trim(),
            IssuedOn = today, ResponseDueOn = r.ResponseDueOn, Status = ScarStatus.Issued, SiteId = s.SiteId, DepartmentId = s.DepartmentId, OwnerId = owner
        };
        db.SupplierCorrectiveActions.Add(c);
        if (r.NcrId is { } nid) db.RecordLinks.Add(new RecordLink { FromType = NcrService.RecordType, FromId = nid, ToType = ScarType, ToId = c.Id, LinkType = "Supplier corrective action" });
        if (r.SourceType is { } srcType) db.RecordLinks.Add(new RecordLink { FromType = srcType, FromId = r.SourceId!.Value, ToType = ScarType, ToId = c.Id, LinkType = "Supplier corrective action" });
        await db.SaveChangesAsync(ct);
        await IndexAsync(c, ct);
        if (owner != m.UserId) await notifier.NotifyAsync([owner], $"SCAR {c.Number} assigned to you", c.Title, await m.RecordAsync(ScarType, c.Id, ct), ct: ct);
        await tx.CommitAsync(ct);
        return c;
    }

    public async Task RecordResponseAsync(Guid id, ScarResponseRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ScarStatus.Issued) throw new ConflictException("A response can only be recorded while the SCAR is waiting for the supplier.");
        ModuleSupport.Required(r.Containment, "Supplier containment", 4000);
        ModuleSupport.Required(r.RootCause, "Supplier root cause", 4000);
        ModuleSupport.Required(r.CorrectiveActionPlan, "Corrective action plan", 4000);
        ModuleSupport.Required(r.Respondent, "Respondent", 200);
        if (r.ReceivedOn < c.IssuedOn || r.ReceivedOn > await m.TodayAsync(c.SiteId, ct)) throw new AppException("The received date must be between the issue date and today.");
        c.SupplierContainment = r.Containment.Trim(); c.SupplierRootCause = r.RootCause.Trim(); c.CorrectiveActionPlan = r.CorrectiveActionPlan.Trim();
        c.SupplierRespondent = r.Respondent.Trim(); c.ResponseReceivedOn = r.ReceivedOn; c.Status = ScarStatus.ResponseReceived;
        await SaveAsync(c, ct);
        await notifier.NotifyAsync([c.OwnerId], $"SCAR {c.Number}: supplier response recorded", "Review the response.", await m.RecordAsync(ScarType, id, ct), ct: ct);
    }

    /// <summary>Accepting moves to effectiveness verification; rejecting returns it to the supplier and counts the rejection.</summary>
    public async Task ReviewResponseAsync(Guid id, ScarReviewRequest r, CancellationToken ct)
    {
        var c = await LoadEditAsync(id, ct);
        if (c.Status != ScarStatus.ResponseReceived) throw new ConflictException("There is no response to review.");
        ModuleSupport.Required(r.Notes, "Review notes", 4000);
        c.ReviewNotes = r.Notes.Trim();
        if (r.Accept) c.Status = ScarStatus.Verification;
        else { c.Status = ScarStatus.Issued; c.ResponseRejections++; c.ResponseReceivedOn = null; }
        await SaveAsync(c, ct);
    }

    /// <summary>Effectiveness is verified by a supplier approver who is not the SCAR owner.</summary>
    public async Task VerifyAsync(Guid id, ScarVerifyRequest r, CancellationToken ct)
    {
        await m.ViewAsync(ScarType, id, ct);
        var c = await db.SupplierCorrectiveActions.FirstAsync(x => x.Id == id, ct);
        await m.RequireAsync(Permissions.SuppliersApprove, c.SiteId, c.DepartmentId, ct);
        if (c.OwnerId == m.UserId) throw new ForbiddenException("The SCAR owner cannot verify their own SCAR.");
        if (c.Status != ScarStatus.Verification) throw new ConflictException("The SCAR is not ready for verification.");
        ModuleSupport.Required(r.Notes, "Verification evidence", 4000);
        c.EffectivenessNotes = r.Notes.Trim(); c.WasEffective = r.Effective; c.VerifiedById = m.UserId;
        if (r.Effective) { c.Status = ScarStatus.Closed; c.ClosedAt = m.Now; }
        else { c.Status = ScarStatus.Issued; c.ResponseReceivedOn = null; }
        await SaveAsync(c, ct);
        await notifier.NotifyAsync([c.OwnerId], r.Effective ? $"SCAR {c.Number} closed as effective" : $"SCAR {c.Number} not effective; reissued", r.Notes, await m.RecordAsync(ScarType, id, ct), ct: ct);
    }

    /// <summary>Records a scorecard for a period. NCR count and SCAR responsiveness come from the records; lots and deliveries are entered.</summary>
    public async Task<SupplierPerformance> RecordPerformanceAsync(Guid supplierId, PerformanceRequest r, CancellationToken ct)
    {
        await m.EditAsync(SupplierService.RecordType, supplierId, ct);
        if (r.PeriodEnd < r.PeriodStart) throw new AppException("The period end must be on or after the start.");
        if (r.LotsReceived < 0 || r.LotsRejected < 0 || r.LotsRejected > r.LotsReceived) throw new AppException("Rejected lots must be between 0 and lots received.");
        if (r.Deliveries < 0 || r.OnTimeDeliveries < 0 || r.OnTimeDeliveries > r.Deliveries) throw new AppException("On-time deliveries must be between 0 and deliveries.");
        if (await db.SupplierPerformance.AnyAsync(p => p.SupplierId == supplierId && p.PeriodStart == r.PeriodStart && p.PeriodEnd == r.PeriodEnd, ct)) throw new ConflictException("A scorecard for this period already exists.");
        var ncrs = await db.Ncrs.CountAsync(n => n.SupplierId == supplierId && n.DetectedOn >= r.PeriodStart && n.DetectedOn <= r.PeriodEnd, ct);
        var scars = await db.SupplierCorrectiveActions.Where(s => s.SupplierId == supplierId && s.ResponseDueOn >= r.PeriodStart && s.ResponseDueOn <= r.PeriodEnd && s.Status != ScarStatus.Cancelled)
            .Select(s => new { s.ResponseDueOn, s.ResponseReceivedOn, s.Status }).ToListAsync(ct);
        var onTime = scars.Count(s => s.ResponseReceivedOn is { } d ? d <= s.ResponseDueOn : s.Status is ScarStatus.Verification or ScarStatus.Closed);
        var (q, d, resp, overall) = SupplierScoring.Score(r.LotsReceived, r.LotsRejected, r.Deliveries, r.OnTimeDeliveries, scars.Count, onTime);
        var p = new SupplierPerformance
        {
            SupplierId = supplierId, PeriodStart = r.PeriodStart, PeriodEnd = r.PeriodEnd, LotsReceived = r.LotsReceived, LotsRejected = r.LotsRejected, Deliveries = r.Deliveries,
            OnTimeDeliveries = r.OnTimeDeliveries, NcrCount = ncrs, ScarsDue = scars.Count, ScarsAnsweredOnTime = onTime, QualityScore = q, DeliveryScore = d, ResponsivenessScore = resp, OverallScore = overall,
            Comments = string.IsNullOrWhiteSpace(r.Comments) ? null : r.Comments.Trim()
        };
        db.SupplierPerformance.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    /// <summary>Periodic review by a supplier approver: snapshots the scorecard, sets the next review date and applies the outcome.</summary>
    public async Task<SupplierReview> RecordReviewAsync(Guid supplierId, SupplierReviewRequest r, CancellationToken ct)
    {
        await m.ViewAsync(SupplierService.RecordType, supplierId, ct);
        var s = await db.Suppliers.FirstAsync(x => x.Id == supplierId, ct);
        await m.RequireAsync(Permissions.SuppliersApprove, s.SiteId, s.DepartmentId, ct);
        ModuleSupport.Required(r.Summary, "Summary", 4000);
        var today = await m.TodayAsync(s.SiteId, ct);
        if (r.NextReviewOn <= today) throw new AppException("The next review must be in the future.");
        if (s.Status is SupplierStatus.Prospective or SupplierStatus.PendingApproval) throw new ConflictException("Only approved, conditional or restricted suppliers are reviewed periodically.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var review = new SupplierReview
        {
            SupplierId = supplierId, ReviewedOn = today, Outcome = r.Outcome, Summary = r.Summary.Trim(), NextReviewOn = r.NextReviewOn, ReviewerId = m.UserId,
            OverallScore = await db.SupplierPerformance.Where(p => p.SupplierId == supplierId).OrderByDescending(p => p.PeriodEnd).Select(p => (decimal?)p.OverallScore).FirstOrDefaultAsync(ct),
            OpenScars = await db.SupplierCorrectiveActions.CountAsync(c => c.SupplierId == supplierId && c.Status != ScarStatus.Closed && c.Status != ScarStatus.Cancelled, ct),
            NcrsLast12Months = await db.Ncrs.CountAsync(n => n.SupplierId == supplierId && n.DetectedOn > today.AddYears(-1), ct)
        };
        db.SupplierReviews.Add(review);
        s.NextReviewOn = r.NextReviewOn;
        if (r.Outcome == ReviewOutcome.Conditional && s.Status != SupplierStatus.Conditional) { s.Status = SupplierStatus.Conditional; s.StatusReason = review.Summary; }
        await db.SaveChangesAsync(ct);
        if (r.Outcome == ReviewOutcome.Suspend && s.Status != SupplierStatus.Suspended) await suppliers.SuspendAsync(supplierId, $"Periodic review: {review.Summary}", ct);
        if (r.Outcome == ReviewOutcome.Disqualify && s.Status != SupplierStatus.Disqualified) await suppliers.DisqualifyAsync(supplierId, $"Periodic review: {review.Summary}", ct);
        await tx.CommitAsync(ct);
        return review;
    }

    private async Task<SupplierCorrectiveAction> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(ScarType, id, ct);
        return await db.SupplierCorrectiveActions.FirstAsync(x => x.Id == id, ct);
    }

    private async Task SaveAsync(SupplierCorrectiveAction c, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await IndexAsync(c, ct);
    }

    private Task IndexAsync(SupplierCorrectiveAction c, CancellationToken ct) => m.IndexAsync(ScarType, c.Id, $"{c.Problem} {c.SupplierRootCause}", ct);
}
