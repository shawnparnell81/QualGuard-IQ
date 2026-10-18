using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using static QualGuardIQ.Infrastructure.Modules.QualityOps.QualityEventService;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record QualityReviewInput(Guid SiteId, Guid? DepartmentId, QualityReviewKind Kind, string Title, Guid? ProductId, Guid? PartId, DateOnly PeriodFrom, DateOnly PeriodTo, Guid? OwnerId);
public sealed record QualityReviewText(string? Conclusions, string? Actions, uint RowVersion = 0);

/// <summary>
/// Product quality review (one product or part) and annual quality review (the site). The metrics are worked out from the module records of
/// the period and kept as a snapshot with its time, so the approved review shows what the approvers saw. Approval uses the shared route.
/// </summary>
public sealed class QualityReviewService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, INotifier notifier)
{
    public const string RecordType = "QualityReview";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<QualityReview> CreateAsync(QualityReviewInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.QualityOpsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.PeriodTo < r.PeriodFrom) throw new AppException("The period cannot end before it starts.");
        if (r.PeriodTo.DayNumber - r.PeriodFrom.DayNumber > 800) throw new AppException("A review period is limited to about two years.");
        if (r.Kind == QualityReviewKind.Product && r.ProductId is null && r.PartId is null) throw new AppException("A product quality review needs the product or part.");
        if (r.ProductId is { } pr && !await db.Products.AnyAsync(e => e.Id == pr, ct)) throw new AppException("The product does not exist.");
        if (r.PartId is { } p && !await db.Parts.AnyAsync(e => e.Id == p, ct)) throw new AppException("The part does not exist.");
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var x = new QualityReview
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Kind = r.Kind, Status = QualityReviewStatus.Draft, Title = r.Title.Trim(), ProductId = r.ProductId, PartId = r.PartId,
            PeriodFrom = r.PeriodFrom, PeriodTo = r.PeriodTo, OwnerId = owner, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.QualityReviews.Add(x);
        await db.SaveChangesAsync(ct);
        await RefreshInternalAsync(x, ct);
        await tx.CommitAsync(ct);
        return x;
    }

    public async Task RefreshAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (QualityReviewStatus.Draft or QualityReviewStatus.Rejected)) throw new ConflictException("Only a draft review can be refreshed.");
        await RefreshInternalAsync(x, ct);
    }

    async Task RefreshInternalAsync(QualityReview x, CancellationToken ct)
    {
        var (start, end) = await m.DayRangeAsync(x.SiteId, x.PeriodFrom, x.PeriodTo, ct);
        var partNumber = x.PartId is { } pid ? await db.Parts.Where(p => p.Id == pid).Select(p => p.PartNumber).FirstOrDefaultAsync(ct) : null;
        var ncr = db.Ncrs.AsNoTracking().Where(n => n.SiteId == x.SiteId && n.CreatedAt >= start && n.CreatedAt < end);
        var cmp = db.Complaints.AsNoTracking().Where(c => c.SiteId == x.SiteId && c.CreatedAt >= start && c.CreatedAt < end);
        var dev = db.Deviations.AsNoTracking().Where(d => d.SiteId == x.SiteId && d.CreatedAt >= start && d.CreatedAt < end);
        var rel = db.ReleaseDecisions.AsNoTracking().Where(d => d.SiteId == x.SiteId && d.DecidedAt >= start && d.DecidedAt < end);
        var capa = db.Capas.AsNoTracking().Where(c => c.SiteId == x.SiteId && c.CreatedAt >= start && c.CreatedAt < end);
        var chg = db.ChangeRequests.AsNoTracking().Where(c => c.SiteId == x.SiteId && c.CreatedAt >= start && c.CreatedAt < end);
        if (x.Kind == QualityReviewKind.Product)
        {
            ncr = ncr.Where(n => (x.PartId != null && n.PartId == x.PartId) || (x.ProductId != null && n.ProductId == x.ProductId));
            cmp = cmp.Where(c => (x.PartId != null && c.PartId == x.PartId) || (x.ProductId != null && c.ProductId == x.ProductId));
            dev = dev.Where(d => (x.PartId != null && d.PartId == x.PartId) || (x.ProductId != null && d.ProductId == x.ProductId));
            rel = rel.Where(d => (x.PartId != null && d.PartId == x.PartId) || (x.ProductId != null && d.ProductId == x.ProductId));
            var ncrIds = ncr.Select(n => n.Id);
            capa = capa.Where(c => db.RecordLinks.Any(l => (l.FromType == NcrService.RecordType && ncrIds.Contains(l.FromId) && l.ToType == CapaService.RecordType && l.ToId == c.Id)
                || (l.ToType == NcrService.RecordType && ncrIds.Contains(l.ToId) && l.FromType == CapaService.RecordType && l.FromId == c.Id)));
            chg = chg.Where(_ => false);
        }
        var metrics = new
        {
            Nonconformances = new { Total = await ncr.CountAsync(ct), Open = await ncr.CountAsync(n => n.Status != NcrStatus.Closed && n.Status != NcrStatus.Cancelled, ct),
                Critical = await ncr.CountAsync(n => n.Severity == Severity.Critical, ct), Quantity = await ncr.SumAsync(n => n.QuantityAffected ?? 0, ct),
                TopDefects = await ncr.Where(n => n.DefectCode != null).GroupBy(n => n.DefectCode!).Select(g => new { Defect = g.Key, Count = g.Count() }).OrderByDescending(g => g.Count).Take(5).ToListAsync(ct) },
            Complaints = new { Total = await cmp.CountAsync(ct), Open = await cmp.CountAsync(c => c.Status != ComplaintStatus.Closed && c.Status != ComplaintStatus.Cancelled, ct),
                TopFailureModes = await cmp.Where(c => c.FailureMode != null).GroupBy(c => c.FailureMode!).Select(g => new { Mode = g.Key, Count = g.Count() }).OrderByDescending(g => g.Count).Take(5).ToListAsync(ct) },
            CorrectiveActions = new { Opened = await capa.CountAsync(ct), Closed = await capa.CountAsync(c => c.Status == CapaStatus.Closed, ct), Effective = await capa.CountAsync(c => c.WasEffective == true, ct),
                NotEffective = await capa.CountAsync(c => c.WasEffective == false, ct) },
            Deviations = new { Total = await dev.CountAsync(ct), Planned = await dev.CountAsync(d => d.Kind == DeviationKind.Planned, ct), Unplanned = await dev.CountAsync(d => d.Kind == DeviationKind.Unplanned, ct),
                Temporary = await dev.CountAsync(d => d.Kind == DeviationKind.Temporary, ct) },
            Releases = new { Released = await rel.CountAsync(d => d.Status == ReleaseStatus.Released, ct), Rejected = await rel.CountAsync(d => d.Status == ReleaseStatus.Rejected, ct),
                QuantityReleased = await rel.Where(d => d.Status == ReleaseStatus.Released).SumAsync(d => d.Quantity ?? 0, ct) },
            Changes = new { Total = await chg.CountAsync(ct), Closed = await chg.CountAsync(c => c.Status == ChangeStatus.Closed, ct) },
            Part = partNumber,
        };
        x.Metrics = JsonSerializer.Serialize(metrics, Json); x.MetricsAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveTextAsync(Guid id, QualityReviewText r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (QualityReviewStatus.Draft or QualityReviewStatus.Rejected)) throw new ConflictException("Only a draft review can be edited.");
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        if (r.Conclusions is { Length: > 10000 } || r.Actions is { Length: > 10000 }) throw new AppException("Conclusions and actions are limited to 10000 characters.");
        x.Conclusions = T(r.Conclusions); x.Actions = T(r.Actions);
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (QualityReviewStatus.Draft or QualityReviewStatus.Rejected)) throw new ConflictException("Only a draft review can be submitted.");
        if (x.Conclusions is null) throw new AppException("Write the conclusions before submitting.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        x.Status = QualityReviewStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var x = await db.QualityReviews.FirstAsync(e => e.Id == id, ct);
        x.Status = QualityReviewStatus.Approved; x.ApprovedAt = m.Now;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.OwnerId], $"Quality review {x.Number} approved", x.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var x = await db.QualityReviews.FirstAsync(e => e.Id == id, ct);
        x.Status = QualityReviewStatus.Rejected;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.OwnerId], $"Quality review {x.Number} returned", comment, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    private async Task<QualityReview> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.QualityReviews.FirstAsync(x => x.Id == id, ct);
    }
}
