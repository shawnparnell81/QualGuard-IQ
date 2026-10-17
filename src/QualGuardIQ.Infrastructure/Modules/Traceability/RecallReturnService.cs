using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Complaints;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Traceability;

public sealed record RecallInput(string Title, string Reason, Severity RiskRating, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, List<RecallItemInput> Items);
public sealed record RecallItemInput(Guid? ProductId, Guid? PartId, Guid? LotId, Guid? SerialId, string? Note);
public sealed record RecallStepInput(string Notes);
public sealed record PartyUpdate(Guid PartyId, bool Notified, bool Contained, string? Response);
public sealed record ReturnInput(Guid CustomerId, Guid? ProductId, Guid? PartId, string? SerialNumber, string? LotNumber, decimal Quantity, string ReturnReason, string? CustomerReference,
    Guid? ComplaintId, Guid SiteId, Guid? DepartmentId, Guid? OwnerId);
public sealed record ReturnReceiveInput(DateOnly ReceivedOn, decimal Quantity, string? Condition);
public sealed record ReturnInspectInput(string InspectionNotes, bool FailureConfirmed, string? RootCause, string? CorrectiveAction);
public sealed record ReturnDispositionInput(ReturnDisposition Disposition, string? Notes);
public sealed record WarrantyInput(Guid CustomerId, Guid? ProductId, Guid? PartId, string? SerialNumber, string? LotNumber, decimal Quantity, DateOnly ClaimedOn, DateOnly? FailedOn,
    string FailureMode, Guid? ComponentPartId, Guid? SupplierId, decimal LaborCost, decimal PartsCost, decimal FreightCost, decimal OtherCost, Guid? ReturnId, Guid? ComplaintId, Guid SiteId, Guid? DepartmentId);
public sealed record WarrantyDecisionInput(bool Approve, string Decision, decimal? SupplierRecovery);
public sealed record WarrantyMetrics(decimal ClaimQuantity, decimal ShippedQuantity, decimal Ppm, decimal TotalCost, decimal SupplierRecovery, int Claims,
    List<WarrantyTrend> Trend, List<WarrantyGroup> ByProduct, List<WarrantyGroup> ByComponent, List<WarrantyGroup> BySupplier);
public sealed record WarrantyTrend(string Period, int Claims, decimal Quantity, decimal Cost, decimal Shipped, decimal Ppm);
public sealed record WarrantyGroup(Guid? Id, string Label, int Claims, decimal Quantity, decimal Cost);

/// <summary>Controlled recalls: scope (products/parts/lots/serials) → trace impacted customers and suppliers → containment → notification → e-signed closure.</summary>
public sealed class RecallService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, TraceabilityService trace, INotifier notifier)
{
    public const string RecordType = "Recall";

    public async Task<Recall> CreateAsync(RecallInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.RecallsEdit, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300); ModuleSupport.Required(r.Reason, "Reason", 4000);
        if (r.Items.Count == 0) throw new AppException("Add at least one affected product, part, lot or serial.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rc = new Recall { Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Title = r.Title.Trim(), Reason = r.Reason.Trim(), RiskRating = r.RiskRating, OwnerId = r.OwnerId ?? m.UserId, SiteId = r.SiteId, DepartmentId = r.DepartmentId };
        db.Recalls.Add(rc);
        foreach (var i in r.Items)
        {
            if (i.ProductId is null && i.PartId is null && i.LotId is null && i.SerialId is null) throw new AppException("Each affected item needs a product, part, lot or serial.");
            db.RecallItems.Add(new RecallItem { RecallId = rc.Id, ProductId = i.ProductId, PartId = i.PartId, LotId = i.LotId, SerialId = i.SerialId, Note = i.Note });
        }
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, rc.Id, rc.Reason, ct);
        await tx.CommitAsync(ct);
        return rc;
    }

    public async Task<List<Recall>> ListAsync(CancellationToken ct) => await (await m.ScopeAsync(db.Recalls.AsNoTracking(), Permissions.RecallsView, ct)).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    public async Task<Recall> GetAsync(Guid id, CancellationToken ct) { await m.ViewAsync(RecordType, id, ct); return await db.Recalls.AsNoTracking().FirstAsync(x => x.Id == id, ct); }
    public async Task<List<RecallItem>> ItemsAsync(Guid id, CancellationToken ct) { await m.ViewAsync(RecordType, id, ct); return await db.RecallItems.AsNoTracking().Where(x => x.RecallId == id).ToListAsync(ct); }
    public async Task<List<RecallParty>> PartiesAsync(Guid id, CancellationToken ct) { await m.ViewAsync(RecordType, id, ct); return await db.RecallParties.AsNoTracking().Where(x => x.RecallId == id).OrderBy(x => x.PartyKind).ToListAsync(ct); }

    /// <summary>Runs the genealogy for every affected item and stores the impacted customers and suppliers (forward to shipments, backward to supplier lots).</summary>
    public async Task<List<RecallParty>> TraceAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        var rc = await db.Recalls.FirstAsync(x => x.Id == id, ct);
        if (rc.Status is RecallStatus.Closed or RecallStatus.Cancelled or RecallStatus.PendingClosure) throw new ConflictException("The recall is no longer open.");
        var items = await db.RecallItems.Where(x => x.RecallId == id).ToListAsync(ct);
        var lotIds = items.Where(i => i.LotId != null).Select(i => i.LotId!.Value).ToList();
        var serIds = items.Where(i => i.SerialId != null).Select(i => i.SerialId!.Value).ToList();
        var partIds = items.Where(i => i.PartId != null).Select(i => i.PartId!.Value).ToList();
        foreach (var p in items.Where(i => i.ProductId != null)) partIds.AddRange(await db.Parts.Where(x => x.ProductId == p.ProductId).Select(x => x.Id).ToListAsync(ct));
        lotIds.AddRange(await db.Lots.Where(l => partIds.Contains(l.PartId)).Select(l => l.Id).ToListAsync(ct));
        serIds.AddRange(await db.SerialUnits.Where(s => partIds.Contains(s.PartId)).Select(s => s.Id).ToListAsync(ct));
        var customers = new Dictionary<Guid, (decimal Q, HashSet<string> L, HashSet<string> S)>();
        var suppliers = new Dictionary<Guid, (decimal Q, HashSet<string> L)>();
        foreach (var (kind, rid) in lotIds.Distinct().Select(x => ("lot", x)).Concat(serIds.Distinct().Select(x => ("serial", x))))
        {
            var g = await trace.GenealogyAsync(kind, rid, ct);
            foreach (var c in g.Customers)
            {
                var cur = customers.TryGetValue(c.Id, out var v) ? v : (Q: 0m, L: new HashSet<string>(), S: new HashSet<string>());
                cur.Q += c.Quantity; cur.L.UnionWith(c.Lots); cur.S.UnionWith(c.Serials); customers[c.Id] = cur;
            }
            foreach (var s in g.Suppliers)
            {
                var cur = suppliers.TryGetValue(s.Id, out var v) ? v : (Q: 0m, L: new HashSet<string>());
                cur.Q += s.Quantity; cur.L.UnionWith(s.Lots); suppliers[s.Id] = cur;
            }
        }
        var existing = await db.RecallParties.Where(x => x.RecallId == id).ToListAsync(ct);
        foreach (var (cid, v) in customers)
        {
            var p = existing.FirstOrDefault(x => x.PartyKind == "Customer" && x.PartyId == cid) ?? db.RecallParties.Add(new RecallParty { RecallId = id, PartyKind = "Customer", PartyId = cid }).Entity;
            p.Quantity = v.Q; p.Lots = string.Join(", ", v.L.Order()); p.Serials = string.Join(", ", v.S.Order());
        }
        foreach (var (sid, v) in suppliers)
        {
            var p = existing.FirstOrDefault(x => x.PartyKind == "Supplier" && x.PartyId == sid) ?? db.RecallParties.Add(new RecallParty { RecallId = id, PartyKind = "Supplier", PartyId = sid }).Entity;
            p.Quantity = v.Q; p.Lots = string.Join(", ", v.L.Order());
        }
        if (rc.Status == RecallStatus.Open) rc.Status = RecallStatus.Investigation;
        await db.SaveChangesAsync(ct);
        return await db.RecallParties.AsNoTracking().Where(x => x.RecallId == id).ToListAsync(ct);
    }

    public async Task ContainAsync(Guid id, RecallStepInput r, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        ModuleSupport.Required(r.Notes, "Containment", 4000);
        var rc = await db.Recalls.FirstAsync(x => x.Id == id, ct);
        if (rc.Status is not (RecallStatus.Investigation or RecallStatus.Containment)) throw new ConflictException("Trace the recall before containment.");
        rc.Containment = r.Notes.Trim(); rc.Status = RecallStatus.Containment;
        // hold every affected lot that is still in stock
        var lotIds = await db.RecallItems.Where(x => x.RecallId == id && x.LotId != null).Select(x => x.LotId!.Value).ToListAsync(ct);
        foreach (var l in await db.Lots.Where(x => lotIds.Contains(x.Id) && x.Status == LotStatus.Active).ToListAsync(ct)) l.Status = LotStatus.OnHold;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdatePartyAsync(Guid id, PartyUpdate r, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        var rc = await db.Recalls.FirstAsync(x => x.Id == id, ct);
        if (rc.Status is RecallStatus.Closed or RecallStatus.Cancelled or RecallStatus.PendingClosure) throw new ConflictException("The recall is no longer open.");
        var p = await db.RecallParties.FirstOrDefaultAsync(x => x.RecallId == id && x.PartyId == r.PartyId, ct) ?? throw new NotFoundException("Impacted party");
        if (r.Notified && p.NotifiedAt is null) p.NotifiedAt = m.Now;
        if (r.Contained && p.ContainedAt is null) p.ContainedAt = m.Now;
        p.Response = r.Response ?? p.Response;
        if (rc.Status == RecallStatus.Containment) rc.Status = RecallStatus.Notification;
        await db.SaveChangesAsync(ct);
    }

    public async Task LinkCapaAsync(Guid id, Guid capaId, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        await m.ViewAsync(CapaService.RecordType, capaId, ct);
        var rc = await db.Recalls.FirstAsync(x => x.Id == id, ct);
        rc.CapaId = capaId;
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = id, ToType = CapaService.RecordType, ToId = capaId, LinkType = "Corrective action" });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Submits for e-signed closure: every impacted customer notified and contained, containment recorded, corrective action linked.</summary>
    public async Task SubmitClosureAsync(Guid id, RecallStepInput r, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        var rc = await db.Recalls.FirstAsync(x => x.Id == id, ct);
        if (rc.Containment is null) throw new AppException("Record containment first.");
        if (await db.RecallParties.AnyAsync(x => x.RecallId == id && x.PartyKind == "Customer" && (x.NotifiedAt == null || x.ContainedAt == null), ct)) throw new AppException("Every impacted customer must be notified and contained before closure.");
        if (rc.CapaId is null) throw new AppException("Link the corrective action (CAPA) first.");
        ModuleSupport.Required(r.Notes, "Closure summary", 4000);
        rc.ClosureSummary = r.Notes.Trim(); rc.Status = RecallStatus.PendingClosure;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var rc = await db.Recalls.FirstAsync(x => x.Id == id, ct);
        rc.Status = RecallStatus.Closed; rc.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([rc.OwnerId], $"Recall {rc.Number} closed", rc.ClosureSummary, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var rc = await db.Recalls.FirstAsync(x => x.Id == id, ct);
        rc.Status = RecallStatus.Notification;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([rc.OwnerId], $"Recall {rc.Number} closure rejected", comment, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }
}

/// <summary>Customer returns (RMA) and warranty claims, integrated with complaints, NCR and CAPA.</summary>
public sealed class ReturnService(AppDbContext db, ModuleSupport m, INumberingService numbers, NcrService ncrs, CapaService capas, ComplaintService complaints, TraceabilityService trace)
{
    public const string ReturnType = "CustomerReturn", WarrantyType = "WarrantyClaim";

    public async Task<CustomerReturn> CreateAsync(ReturnInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.ReturnsEdit, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.ReturnReason, "Return reason", 2000);
        if (r.Quantity <= 0) throw new AppException("Quantity must be positive.");
        if (!await db.Customers.AnyAsync(x => x.Id == r.CustomerId, ct)) throw new AppException("Select a customer.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ret = new CustomerReturn
        {
            Number = await numbers.NextAsync(ReturnType, r.SiteId, ct), CustomerId = r.CustomerId, ProductId = r.ProductId, PartId = r.PartId, SerialNumber = r.SerialNumber?.Trim(), LotNumber = r.LotNumber?.Trim(),
            Quantity = r.Quantity, ReturnReason = r.ReturnReason.Trim(), CustomerReference = r.CustomerReference, AuthorizedOn = await m.TodayAsync(r.SiteId, ct), ComplaintId = r.ComplaintId,
            OwnerId = r.OwnerId ?? m.UserId, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.CustomerReturns.Add(ret);
        if (r.ComplaintId is { } cid)
        {
            await m.EditAsync(ComplaintService.RecordType, cid, ct);
            var c = await db.Complaints.FirstAsync(x => x.Id == cid, ct);
            c.ReturnAuthorization = ret.Number; c.ReturnExpected = true;
            db.RecordLinks.Add(new RecordLink { FromType = ComplaintService.RecordType, FromId = cid, ToType = ReturnType, ToId = ret.Id, LinkType = "Return" });
        }
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ReturnType, ret.Id, string.Join(' ', new[] { ret.ReturnReason, ret.SerialNumber, ret.LotNumber, ret.CustomerReference }.Where(x => x is not null)), ct);
        await tx.CommitAsync(ct);
        return ret;
    }

    public async Task<List<CustomerReturn>> ListAsync(CancellationToken ct) => await (await m.ScopeAsync(db.CustomerReturns.AsNoTracking(), Permissions.ReturnsView, ct)).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    public async Task<CustomerReturn> GetAsync(Guid id, CancellationToken ct) { await m.ViewAsync(ReturnType, id, ct); return await db.CustomerReturns.AsNoTracking().FirstAsync(x => x.Id == id, ct); }

    async Task<CustomerReturn> EditAsync(Guid id, CancellationToken ct, params ReturnStatus[] allowed)
    {
        await m.EditAsync(ReturnType, id, ct);
        var r = await db.CustomerReturns.FirstAsync(x => x.Id == id, ct);
        if (!allowed.Contains(r.Status)) throw new ConflictException($"Not allowed while the return is {r.Status}.");
        return r;
    }

    public async Task ReceiveAsync(Guid id, ReturnReceiveInput i, CancellationToken ct)
    {
        var r = await EditAsync(id, ct, ReturnStatus.Authorized);
        if (i.Quantity <= 0) throw new AppException("Quantity must be positive.");
        if (i.ReceivedOn > await m.TodayAsync(r.SiteId, ct)) throw new AppException("The receipt date cannot be in the future.");
        r.ReceivedOn = i.ReceivedOn; r.Quantity = i.Quantity; r.Condition = i.Condition; r.Status = ReturnStatus.Received;
        if (r.ComplaintId is { } cid)
        {
            var c = await db.Complaints.FirstAsync(x => x.Id == cid, ct);
            c.ReturnReceivedOn = i.ReceivedOn; c.ReturnQuantity = i.Quantity; c.ReturnCondition = i.Condition;
        }
        // serial history: returned
        if (r.SerialNumber is { } sn && await db.SerialUnits.FirstOrDefaultAsync(x => x.SerialNumber == sn && (r.PartId == null || x.PartId == r.PartId), ct) is { } s)
            await trace.RecordAsync(new TraceEventInput(TraceEventKind.Return, s.Id, null, null, null, 1, null, null, null, r.CustomerId, ReturnType, r.Id, r.Number, i.Condition, null), ct, checkAccess: false);
        await db.SaveChangesAsync(ct);
    }

    public async Task InspectAsync(Guid id, ReturnInspectInput i, CancellationToken ct)
    {
        var r = await EditAsync(id, ct, ReturnStatus.Received, ReturnStatus.Inspected);
        ModuleSupport.Required(i.InspectionNotes, "Inspection notes", 4000);
        r.InspectionNotes = i.InspectionNotes.Trim(); r.FailureConfirmed = i.FailureConfirmed; r.RootCause = i.RootCause; r.CorrectiveAction = i.CorrectiveAction; r.Status = ReturnStatus.Inspected;
        if (r.ComplaintId is { } cid) { var c = await db.Complaints.FirstAsync(x => x.Id == cid, ct); c.ReturnEvaluation = i.InspectionNotes; c.FailureConfirmed = i.FailureConfirmed; }
        await db.SaveChangesAsync(ct);
    }

    public async Task DispositionAsync(Guid id, ReturnDispositionInput i, CancellationToken ct)
    {
        var r = await EditAsync(id, ct, ReturnStatus.Inspected);
        if (i.Disposition == ReturnDisposition.None) throw new AppException("Choose a disposition.");
        if (r.FailureConfirmed == true && r.NcrId is null) throw new AppException("A confirmed failure needs an NCR before disposition.");
        r.Disposition = i.Disposition; r.DispositionNotes = i.Notes; r.Status = ReturnStatus.Dispositioned;
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(Guid id, CancellationToken ct)
    {
        var r = await EditAsync(id, ct, ReturnStatus.Dispositioned);
        r.Status = ReturnStatus.Closed;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Creates the customer complaint for a return that arrived without one.</summary>
    public async Task<Complaint> CreateComplaintAsync(Guid id, CancellationToken ct)
    {
        var r = await EditAsync(id, ct, ReturnStatus.Authorized, ReturnStatus.Received, ReturnStatus.Inspected);
        if (r.ComplaintId is not null) throw new ConflictException("This return already has a complaint.");
        var today = await m.TodayAsync(r.SiteId, ct);
        var c = await complaints.CreateAsync(new ComplaintRequest($"Return {r.Number}: {Short(r.ReturnReason)}", r.ReturnReason, r.CustomerId, null, r.CustomerReference, r.AuthorizedOn, today,
            r.ProductId, r.PartId, null, null, r.LotNumber, null, r.SerialNumber, r.Quantity, null, Severity.Major, Severity.Major, WarrantyStatus.Unknown, null, r.SiteId, r.DepartmentId, r.OwnerId,
            today.AddDays(14), r.Number, true), ct);
        r.ComplaintId = c.Id;
        db.RecordLinks.Add(new RecordLink { FromType = ComplaintService.RecordType, FromId = c.Id, ToType = ReturnType, ToId = r.Id, LinkType = "Return" });
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task<Ncr> CreateNcrAsync(Guid id, CancellationToken ct)
    {
        var r = await EditAsync(id, ct, ReturnStatus.Received, ReturnStatus.Inspected);
        if (r.NcrId is not null) throw new ConflictException("This return already has an NCR.");
        var pn = r.PartId is { } pid ? await db.Parts.Where(x => x.Id == pid).Select(x => x.PartNumber).FirstOrDefaultAsync(ct) : null;
        var n = await ncrs.CreateAsync(new CreateNcrRequest($"Customer return {r.Number}", $"{r.ReturnReason}\n\n{r.InspectionNotes}".Trim(), "Customer return", r.SiteId, r.DepartmentId, r.OwnerId,
            pn, r.LotNumber, r.Quantity, r.ReceivedOn ?? await m.TodayAsync(r.SiteId, ct), Severity.Major, new NcrDetails(ProductId: r.ProductId, PartId: r.PartId, CustomerId: r.CustomerId, SerialNumber: r.SerialNumber,
            DetectionPoint: "Customer")), ct);
        r.NcrId = n.Id;
        db.RecordLinks.Add(new RecordLink { FromType = ReturnType, FromId = id, ToType = NcrService.RecordType, ToId = n.Id, LinkType = "Nonconformance" });
        if (r.ComplaintId is { } cid) db.RecordLinks.Add(new RecordLink { FromType = ComplaintService.RecordType, FromId = cid, ToType = NcrService.RecordType, ToId = n.Id, LinkType = "Nonconformance" });
        await db.SaveChangesAsync(ct);
        return n;
    }

    public async Task<Capa> CreateCapaAsync(Guid id, CancellationToken ct)
    {
        var r = await EditAsync(id, ct, ReturnStatus.Received, ReturnStatus.Inspected, ReturnStatus.Dispositioned);
        if (r.CapaId is not null) throw new ConflictException("This return already has a CAPA.");
        var c = await capas.CreateAsync(new CreateCapaRequest($"Corrective action for return {r.Number}", $"{r.ReturnReason}\nRoot cause: {r.RootCause ?? "under investigation"}", "Corrective", "Customer return", r.SiteId, r.DepartmentId, r.OwnerId),
            ct, r.NcrId is { } nid ? (NcrService.RecordType, nid) : (ReturnType, id));
        r.CapaId = c.Id;
        db.RecordLinks.Add(new RecordLink { FromType = ReturnType, FromId = id, ToType = CapaService.RecordType, ToId = c.Id, LinkType = "Corrective action" });
        await db.SaveChangesAsync(ct);
        return c;
    }

    static string Short(string s) => s.Length > 80 ? s[..80] : s;

    // ---------------------------------------------------------------- warranty
    public async Task<WarrantyClaim> CreateClaimAsync(WarrantyInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.ReturnsEdit, r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.FailureMode, "Failure mode", 500);
        if (r.Quantity <= 0) throw new AppException("Quantity must be positive.");
        if (new[] { r.LaborCost, r.PartsCost, r.FreightCost, r.OtherCost }.Any(x => x < 0)) throw new AppException("Costs cannot be negative.");
        if (r.ClaimedOn > await m.TodayAsync(r.SiteId, ct)) throw new AppException("The claim date cannot be in the future.");
        if (r.FailedOn > r.ClaimedOn) throw new AppException("The failure date cannot be after the claim date.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var w = new WarrantyClaim
        {
            Number = await numbers.NextAsync(WarrantyType, r.SiteId, ct), CustomerId = r.CustomerId, ProductId = r.ProductId, PartId = r.PartId, SerialNumber = r.SerialNumber, LotNumber = r.LotNumber, Quantity = r.Quantity,
            ClaimedOn = r.ClaimedOn, FailedOn = r.FailedOn, FailureMode = r.FailureMode.Trim(), ComponentPartId = r.ComponentPartId, SupplierId = r.SupplierId, LaborCost = r.LaborCost, PartsCost = r.PartsCost,
            FreightCost = r.FreightCost, OtherCost = r.OtherCost, TotalCost = r.LaborCost + r.PartsCost + r.FreightCost + r.OtherCost, ReturnId = r.ReturnId, ComplaintId = r.ComplaintId, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.WarrantyClaims.Add(w);
        if (r.ReturnId is { } rid)
        {
            var ret = await db.CustomerReturns.FirstAsync(x => x.Id == rid, ct);
            ret.WarrantyClaimId = w.Id; w.NcrId = ret.NcrId; w.CapaId = ret.CapaId; w.ComplaintId ??= ret.ComplaintId;
            db.RecordLinks.Add(new RecordLink { FromType = ReturnType, FromId = rid, ToType = WarrantyType, ToId = w.Id, LinkType = "Warranty claim" });
        }
        if (w.ComplaintId is { } cid)
        {
            var c = await db.Complaints.FirstAsync(x => x.Id == cid, ct);
            c.Warranty = WarrantyStatus.InWarranty;
            db.RecordLinks.Add(new RecordLink { FromType = ComplaintService.RecordType, FromId = cid, ToType = WarrantyType, ToId = w.Id, LinkType = "Warranty claim" });
        }
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(WarrantyType, w.Id, string.Join(' ', new[] { w.FailureMode, w.SerialNumber, w.LotNumber }.Where(x => x is not null)), ct);
        await tx.CommitAsync(ct);
        return w;
    }

    public async Task<List<WarrantyClaim>> ClaimsAsync(CancellationToken ct) => await (await m.ScopeAsync(db.WarrantyClaims.AsNoTracking(), Permissions.ReturnsView, ct)).OrderByDescending(x => x.ClaimedOn).Take(500).ToListAsync(ct);
    public async Task<WarrantyClaim> ClaimAsync(Guid id, CancellationToken ct) { await m.ViewAsync(WarrantyType, id, ct); return await db.WarrantyClaims.AsNoTracking().FirstAsync(x => x.Id == id, ct); }

    public async Task DecideClaimAsync(Guid id, WarrantyDecisionInput r, CancellationToken ct)
    {
        var w = await db.WarrantyClaims.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Warranty claim");
        await m.RequireAsync(Permissions.ReturnsApprove, w.SiteId, w.DepartmentId, ct);
        if (w.Status is not (WarrantyClaimStatus.Submitted or WarrantyClaimStatus.UnderReview)) throw new ConflictException("The claim is already decided.");
        if (w.CreatedById == m.UserId) throw new ForbiddenException("You cannot decide a claim you entered.");
        ModuleSupport.Required(r.Decision, "Decision", 2000);
        if (r.SupplierRecovery is < 0) throw new AppException("Supplier recovery cannot be negative.");
        w.Status = r.Approve ? WarrantyClaimStatus.Approved : WarrantyClaimStatus.Rejected; w.Decision = r.Decision.Trim(); w.SupplierRecovery = r.SupplierRecovery;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Warranty PPM = claimed units / shipped units × 10⁶ for the window (shipped from shipment trace events); trend by month; cost; product, component and supplier failures.</summary>
    public async Task<WarrantyMetrics> MetricsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var claims = await (await m.ScopeAsync(db.WarrantyClaims.AsNoTracking(), Permissions.ReturnsView, ct)).Where(x => x.ClaimedOn >= from && x.ClaimedOn <= to && x.Status != WarrantyClaimStatus.Rejected).ToListAsync(ct);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var ships = await (await m.ScopeAsync(db.TraceEvents.AsNoTracking(), Permissions.ReturnsView, ct)).Where(e => e.Kind == TraceEventKind.Shipment && e.OccurredAt >= start && e.OccurredAt <= end).Select(e => new { e.OccurredAt, e.Quantity }).ToListAsync(ct);
        var shipped = ships.Sum(s => s.Quantity ?? 0);
        decimal Ppm(decimal q, decimal s) => s == 0 ? 0 : Math.Round(q / s * 1_000_000m, 1);
        var months = new List<WarrantyTrend>();
        for (var d = new DateOnly(from.Year, from.Month, 1); d <= to; d = d.AddMonths(1))
        {
            var c = claims.Where(x => x.ClaimedOn.Year == d.Year && x.ClaimedOn.Month == d.Month).ToList();
            var s = ships.Where(x => x.OccurredAt.Year == d.Year && x.OccurredAt.Month == d.Month).Sum(x => x.Quantity ?? 0);
            months.Add(new($"{d:yyyy-MM}", c.Count, c.Sum(x => x.Quantity), c.Sum(x => x.TotalCost), s, Ppm(c.Sum(x => x.Quantity), s)));
        }
        var partIds = claims.SelectMany(c => new[] { c.PartId, c.ComponentPartId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToList();
        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.PartNumber, ct);
        var supIds = claims.Where(c => c.SupplierId != null).Select(c => c.SupplierId!.Value).Distinct().ToList();
        var sups = await db.Suppliers.AsNoTracking().Where(s => supIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.LegalName, ct);
        List<WarrantyGroup> G(Func<WarrantyClaim, Guid?> key, Func<Guid?, string> label) => claims.GroupBy(key).Select(g => new WarrantyGroup(g.Key, label(g.Key), g.Count(), g.Sum(x => x.Quantity), g.Sum(x => x.TotalCost))).OrderByDescending(x => x.Cost).ToList();
        return new WarrantyMetrics(claims.Sum(c => c.Quantity), shipped, Ppm(claims.Sum(c => c.Quantity), shipped), claims.Sum(c => c.TotalCost), claims.Sum(c => c.SupplierRecovery ?? 0), claims.Count, months,
            G(c => c.PartId, k => k is { } x && parts.TryGetValue(x, out var p) ? p : "Unspecified"), G(c => c.ComponentPartId, k => k is { } x && parts.TryGetValue(x, out var p) ? p : "Product-level"),
            G(c => c.SupplierId, k => k is { } x && sups.TryGetValue(x, out var s) ? s : "No supplier"));
    }
}
