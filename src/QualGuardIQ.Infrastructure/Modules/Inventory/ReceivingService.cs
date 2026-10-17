using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Fai;
using QualGuardIQ.Infrastructure.Modules.Inspection;
using QualGuardIQ.Infrastructure.Modules.MasterData;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Traceability;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Inventory;

public sealed record ReceiptLineInput(Guid PartId, string? Revision, Guid? PurchaseOrderLineId, decimal? QuantityExpected, decimal QuantityReceived, string? SupplierLotNumber, string? LotNumber,
    List<string>? SerialNumbers, bool? InspectionRequired, Guid? InspectionPlanId, Guid? AqlPlanId, string? AqlInspectionLevel, decimal? AqlLevel, Guid? PutAwayLocationId, string? ClientRequestId,
    bool? PlaceHold = null, string? HoldReason = null);
public sealed record ReceiptInput(ReceiptKind Kind, Guid? SupplierId, Guid? PurchaseOrderId, string? AsnNumber, string? PackingSlip, Guid? CarrierId, string? TrackingNumber, DateOnly ReceivedOn,
    Guid ReceivingLocationId, string? Notes, string? SupplierOverrideReason, Guid SiteId, Guid? DepartmentId, List<ReceiptLineInput> Lines, string? Channel = null);
public enum LineDecision { Accept = 0, Reject = 1, Quarantine = 2 }
public sealed record LineDecisionInput(LineDecision Decision, decimal? QuantityAccepted, string? Notes, Guid? PutAwayLocationId, Guid? QuarantineLocationId, HoldKind? HoldKind);
public sealed record LinkInspectionInput(Guid? InspectionId, Guid? AqlInspectionId);
public sealed record ReceiptDetail(Receipt Receipt, List<ReceiptLineRow> Lines);
public sealed record ReceiptLineRow(ReceiptLine Line, string PartNumber, string? PartName, string? LotNumber, string? InspectionNumber, string? InspectionStatus, string? AqlNumber, string? AqlResult,
    string? NcrNumber, string? HoldNumber, string? PoLine);
public sealed record MrbInput(Guid PartId, Guid? LotId, Guid? SerialId, Guid LocationId, StockStatus StockStatus, decimal Quantity, Guid? NcrId, Guid? HoldId, string? Notes);
public sealed record MrbProposal(MrbDisposition Disposition, string Justification, Guid? TargetLocationId);
public sealed record RtvShipInput(Guid? CarrierId, string? TrackingNumber, DateOnly ShippedOn, string? SupplierRma);
public sealed record RtvCreditInput(decimal CreditAmount, string CreditReference, DateOnly CreditedOn);
public sealed record ReturnStockInput(Guid ReturnId, Guid LocationId, Guid PartId);

/// <summary>
/// Receiving (PO receipts, supplier deliveries, ASNs, blind receipts) connected to the shared inspection engine, AQL sampling, NCR
/// and supplier quality; quarantine and holds; Material Review Board; returns to vendor.
/// </summary>
public sealed partial class ReceivingService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, InventoryService inv, TraceabilityService trace,
    InspectionService inspections, AqlService aql, NcrService ncrs, SupplierService suppliers, INotifier notifier)
{
    public const string ReceiptType = "Receipt", MrbType = "MrbCase", RtvType = "SupplierReturn";

    public async Task<Receipt> CreateAsync(ReceiptInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.InventoryEdit, r.SiteId, r.DepartmentId, ct);
        if (r.Lines is null || r.Lines.Count == 0) throw new AppException("Add at least one received line.");
        // blind receipts (no PO, delivery or ASN) are a separately granted workflow
        if (r.Kind == ReceiptKind.Blind)
        {
            await m.RequireAsync(Permissions.InventoryReceiveBlind, r.SiteId, r.DepartmentId, ct);
            if (r.PurchaseOrderId is not null) throw new AppException("A blind receipt has no purchase order; receive against the PO instead.");
        }
        var channel = r.Channel?.Trim() is { Length: > 0 } ch ? ch switch { "ScanStation" or "Desktop" or "Integration" => ch, _ => throw new AppException("Channel is Desktop, ScanStation or Integration.") } : "Desktop";
        // offline replay: a line request id already posted means this receipt was booked; return it instead of receiving twice
        var replayIds = r.Lines.Select(l => l.ClientRequestId).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        // two replays of the same offline receipt (two tabs, a retry racing the first post) wait for each other, so only one books it
        await using var replayTx = replayIds.Count > 0 ? await db.Database.BeginTransactionAsync(ct) : null;
        foreach (var rid in replayIds.Distinct().Order(StringComparer.Ordinal))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"inventory-request:" + rid}, 0))", ct);
        if (replayIds.Count > 0 && await db.InventoryTransactions.AsNoTracking().Where(x => replayIds.Contains(x.ClientRequestId) && x.RefType == ReceiptType).Select(x => x.RefId).FirstOrDefaultAsync(ct) is { } prior
            && await db.Receipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == prior, ct) is { } booked)
            return booked;
        if (r.ReceivedOn > await m.TodayAsync(r.SiteId, ct)) throw new AppException("The receipt date cannot be in the future.");
        var recvLoc = await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.ReceivingLocationId, ct) ?? throw new AppException("Select the receiving location.");
        if (recvLoc.SiteId != r.SiteId) throw new AppException("The receiving location is at another site.");
        if (recvLoc.Purpose is not (LocationPurpose.Receiving or LocationPurpose.Inspection)) throw new AppException($"{recvLoc.Code} is not a receiving or inspection location.");
        PurchaseOrder? po = null; List<PurchaseOrderLine> poLines = [];
        if (r.Kind == ReceiptKind.PurchaseOrder && r.PurchaseOrderId is null) throw new AppException("A PO receipt needs the purchase order.");
        if (r.Kind == ReceiptKind.Asn && string.IsNullOrWhiteSpace(r.AsnNumber)) throw new AppException("Enter the ASN number.");
        if (r.PurchaseOrderId is { } poId)
        {
            po = await db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == poId, ct) ?? throw new AppException("Unknown purchase order.");
            if (po.Status is PurchaseOrderStatus.Closed or PurchaseOrderStatus.Cancelled) throw new ConflictException($"PO {po.Number} is {po.Status}.");
            poLines = await db.PurchaseOrderLines.Where(x => x.PurchaseOrderId == poId).ToListAsync(ct);
        }
        var supplierId = r.SupplierId ?? po?.SupplierId;
        if (r.Kind is ReceiptKind.PurchaseOrder or ReceiptKind.SupplierDelivery or ReceiptKind.Asn && supplierId is null) throw new AppException("Select the supplier.");
        if (po is not null && supplierId != po.SupplierId) throw new AppException($"PO {po.Number} is with a different supplier.");
        await using var ownTx = replayTx is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var tx = replayTx ?? ownTx!;
        var rc = new Receipt
        {
            Number = await numbers.NextAsync(ReceiptType, r.SiteId, ct), Kind = r.Kind, Status = ReceiptStatus.Received, SupplierId = supplierId, PurchaseOrderId = po?.Id, PurchaseOrderNumber = po?.Number,
            AsnNumber = r.AsnNumber?.Trim(), PackingSlip = r.PackingSlip?.Trim(), CarrierId = r.CarrierId, TrackingNumber = r.TrackingNumber?.Trim(), ReceiverId = m.UserId, ReceivedOn = r.ReceivedOn,
            ReceivingLocationId = recvLoc.Id, Notes = r.Notes, Channel = channel, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.Receipts.Add(rc);
        await db.SaveChangesAsync(ct);
        if (supplierId is { } sid) await suppliers.EnforceAsync(sid, ReceiptType, rc.Id, r.SupplierOverrideReason, ct);
        var policy = await EffectivePolicyAsync(r.SiteId, ct);
        var receiptSerials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var li in r.Lines)
        {
            n++;
            // the same checks the scan station runs before posting (PoReceiving.CheckLineAsync)
            var chk = await CheckLineAsync(n, li, po, poLines, policy, supplierId, r.SiteId, receiptSerials, null, ct);
            if (chk.Errors.FirstOrDefault() is { } bad) throw bad.Conflict ? new ConflictException(bad.Message) : new AppException(bad.Message);
            var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == li.PartId, ct);
            var pol = chk.PurchaseOrderLineId is { } plid ? poLines.First(x => x.Id == plid) : null;
            if (pol is not null) pol.QuantityReceived += li.QuantityReceived;
            var revision = chk.Revision ?? part.Revision;
            var serials = li.SerialNumbers?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [];
            var req = chk.Requirement;
            var plan = li.InspectionPlanId ?? req.InspectionPlanId;
            var aqlPlan = li.AqlPlanId ?? req.AqlPlanId;
            var reqRow = req.Id is { } rqid ? await db.ReceivingRequirements.AsNoTracking().FirstAsync(x => x.Id == rqid, ct) : null;
            var inspect = li.InspectionRequired ?? (req.InspectionRequired || plan is not null || aqlPlan is not null);
            var lot = await trace.CreateLotAsync(new LotInput(li.LotNumber, supplierId is null ? LotKind.RawMaterial : LotKind.Supplier, part.Id, revision, supplierId, li.SupplierLotNumber, null, null, li.QuantityReceived,
                null, null, null, null, po?.Number, r.SiteId, r.DepartmentId), ct);
            var line = new ReceiptLine
            {
                ReceiptId = rc.Id, LineNumber = n, PartId = part.Id, Revision = revision, PurchaseOrderLineId = pol?.Id, QuantityExpected = li.QuantityExpected ?? (pol is null ? li.QuantityReceived : pol.Quantity),
                QuantityReceived = li.QuantityReceived, SupplierLotNumber = li.SupplierLotNumber?.Trim(), LotId = lot.Id, SerialNumbers = serials.Count == 0 ? null : string.Join("\n", serials),
                InspectionRequired = inspect, PutAwayLocationId = li.PutAwayLocationId, Status = inspect ? ReceiptLineStatus.AwaitingInspection : ReceiptLineStatus.Accepted,
                OverReceivedQuantity = chk.OverReceived, ReceivingRequirementId = req.Id
            };
            db.ReceiptLines.Add(line);
            await db.SaveChangesAsync(ct);
            var toLoc = inspect ? recvLoc.Id : li.PutAwayLocationId ?? recvLoc.Id;
            var status = inspect ? StockStatus.AwaitingInspection : StockStatus.Available;
            if (!inspect && toLoc == recvLoc.Id && recvLoc.Purpose == LocationPurpose.Inspection) throw new AppException($"Line {n}: name the put-away location for material that needs no inspection.");
            if (serials.Count > 0)
                foreach (var sn in serials)
                {
                    var s = await trace.CreateSerialAsync(new SerialInput(sn, part.Id, revision, null, lot.Id, null, null, null, r.SiteId, r.DepartmentId), ct);
                    s.Status = SerialStatus.InStock;
                    await inv.PostAsync(new Move(InventoryTxKind.Receipt, part.Id, 1, lot.Id, s.Id, ToLocationId: toLoc, ToStatus: status, RefType: ReceiptType, RefId: rc.Id, RefNumber: rc.Number), ct);
                }
            else await inv.PostAsync(new Move(InventoryTxKind.Receipt, part.Id, li.QuantityReceived, lot.Id, ToLocationId: toLoc, ToStatus: status, RefType: ReceiptType, RefId: rc.Id, RefNumber: rc.Number, ClientRequestId: li.ClientRequestId), ct);
            line.Category = (await db.InventoryBalances.Where(b => b.LotId == lot.Id).Select(b => (InventoryCategory?)b.Category).FirstOrDefaultAsync(ct)) ?? InventoryCategory.Component;
            if (!inspect) { line.QuantityAccepted = li.QuantityReceived; line.DecisionNotes = "No inspection required"; }
            // receipt → receiving inspection (shared engine) → AQL sample on the same record
            if (inspect && plan is { } pid)
            {
                var ins = await inspections.CreateInspectionAsync(new CreateInspectionRequest(pid, supplierId, null, po?.Number, null, lot.Number, li.SupplierLotNumber, serials.FirstOrDefault(), li.QuantityReceived, null,
                    r.ReceivedOn, revision, r.SupplierOverrideReason, r.DepartmentId), ct);
                line.InspectionId = ins.Id;
                db.RecordLinks.Add(new RecordLink { FromType = ReceiptType, FromId = rc.Id, ToType = InspectionService.RecordType, ToId = ins.Id, LinkType = "Receiving inspection" });
            }
            if (inspect && aqlPlan is { } ap)
            {
                var a = await aql.CreateAsync(new AqlLotInput(ap, line.InspectionId, line.InspectionId is null ? part.Id : null, revision, line.InspectionId is null ? supplierId : null, null, null,
                    line.InspectionId is null ? po?.Number : null, null, line.InspectionId is null ? lot.Number : null, (int)Math.Ceiling(li.QuantityReceived),
                    li.AqlInspectionLevel ?? reqRow?.AqlInspectionLevel ?? "II", li.AqlLevel ?? reqRow?.AqlLevel ?? 1.0m, null, null, null,
                    $"Receipt {rc.Number} line {n}"), ct);
                line.AqlInspectionId = a.Id;
            }
            // receiving hold: required for the part (receiving requirement) or placed by the receiver; blocks issue and shipment until released
            if (req.HoldOnReceipt || li.PlaceHold == true)
            {
                var reason = li.PlaceHold == true ? li.HoldReason!.Trim() : $"Receiving hold: {req.HoldReason}";
                var hold = await inv.CreateHoldAsync(new HoldInput(HoldKind.Quality, $"{reason} (receipt {rc.Number} line {n})", part.Id, lot.Id, null, null, null, null, supplierId, r.SiteId, r.DepartmentId), ct,
                    checkAccess: false, receiptLineId: line.Id);
                line.HoldId = hold.Id;
            }
            await db.SaveChangesAsync(ct);
        }
        if (po is not null)
        {
            po.Status = poLines.All(x => x.QuantityReceived >= x.Quantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
            db.RecordLinks.Add(new RecordLink { FromType = "PurchaseOrder", FromId = po.Id, ToType = ReceiptType, ToId = rc.Id, LinkType = "Receipt" });
        }
        rc.Status = await db.ReceiptLines.AnyAsync(x => x.ReceiptId == rc.Id && x.Status == ReceiptLineStatus.AwaitingInspection, ct) ? ReceiptStatus.Inspecting : ReceiptStatus.Completed;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ReceiptType, rc.Id, string.Join(' ', new[] { rc.PurchaseOrderNumber, rc.AsnNumber, rc.PackingSlip, rc.TrackingNumber }.Where(x => x is not null)), ct);
        await tx.CommitAsync(ct);
        return rc;
    }

    public async Task<List<Receipt>> ListAsync(ReceiptStatus? status, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.Receipts.AsNoTracking(), Permissions.InventoryView, ct);
        if (status is { } s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<ReceiptDetail> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(ReceiptType, id, ct);
        var rc = await db.Receipts.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var lines = await db.ReceiptLines.AsNoTracking().Where(x => x.ReceiptId == id).OrderBy(x => x.LineNumber).ToListAsync(ct);
        var rows = new List<ReceiptLineRow>();
        foreach (var l in lines)
        {
            var p = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == l.PartId, ct);
            var ins = l.InspectionId is { } i ? await db.Inspections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == i, ct) : null;
            var a = l.AqlInspectionId is { } ai ? await db.AqlInspections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ai, ct) : null;
            rows.Add(new ReceiptLineRow(l, p.PartNumber, p.Name, l.LotId is { } lid ? await db.Lots.Where(x => x.Id == lid).Select(x => x.Number).FirstOrDefaultAsync(ct) : null,
                ins?.Number, ins?.Status.ToString(), a?.InspectionNumber, a?.OverallResult?.ToString() ?? a?.Status.ToString(),
                l.NcrId is { } n ? await db.Ncrs.Where(x => x.Id == n).Select(x => x.Number).FirstOrDefaultAsync(ct) : null,
                l.HoldId is { } h ? await db.InventoryHolds.Where(x => x.Id == h).Select(x => x.Number).FirstOrDefaultAsync(ct) : null,
                l.PurchaseOrderLineId is { } pl ? await db.PurchaseOrderLines.Where(x => x.Id == pl).Select(x => x.LineNumber.ToString()).FirstOrDefaultAsync(ct) : null));
        }
        return new ReceiptDetail(rc, rows);
    }

    async Task<(Receipt Rc, ReceiptLine Line)> EditLineAsync(Guid receiptId, Guid lineId, CancellationToken ct)
    {
        await m.EditAsync(ReceiptType, receiptId, ct);
        var rc = await db.Receipts.FirstAsync(x => x.Id == receiptId, ct);
        var line = await db.ReceiptLines.FirstOrDefaultAsync(x => x.Id == lineId && x.ReceiptId == receiptId, ct) ?? throw new NotFoundException("Receipt line");
        return (rc, line);
    }

    /// <summary>Links an inspection or AQL lot created in its own module to a line awaiting inspection.</summary>
    public async Task LinkInspectionAsync(Guid receiptId, Guid lineId, LinkInspectionInput r, CancellationToken ct)
    {
        var (_, line) = await EditLineAsync(receiptId, lineId, ct);
        if (line.Status != ReceiptLineStatus.AwaitingInspection) throw new ConflictException("The line is no longer awaiting inspection.");
        if (r.InspectionId is { } i) { await m.ViewAsync(InspectionService.RecordType, i, ct); line.InspectionId = i; db.RecordLinks.Add(new RecordLink { FromType = ReceiptType, FromId = receiptId, ToType = InspectionService.RecordType, ToId = i, LinkType = "Receiving inspection" }); }
        if (r.AqlInspectionId is { } a) { await m.ViewAsync(AqlService.RecordType, a, ct); line.AqlInspectionId = a; }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Inspection outcome for a line: Accept → available stock at the put-away location; Reject → rejected stock in quarantine with a quality hold
    /// and an NCR (the inspection's NCR when there is one); Quarantine → quarantined stock with a hold. Partial acceptance rejects the remainder.
    /// </summary>
    public async Task DecideLineAsync(Guid receiptId, Guid lineId, LineDecisionInput r, CancellationToken ct)
    {
        var (rc, line) = await EditLineAsync(receiptId, lineId, ct);
        if (line.Status != ReceiptLineStatus.AwaitingInspection) throw new ConflictException("The line is not awaiting inspection.");
        var ins = line.InspectionId is { } iid ? await db.Inspections.AsNoTracking().FirstAsync(x => x.Id == iid, ct) : null;
        var a = line.AqlInspectionId is { } aid ? await db.AqlInspections.AsNoTracking().FirstAsync(x => x.Id == aid, ct) : null;
        var accept = r.Decision == LineDecision.Accept ? r.QuantityAccepted ?? line.QuantityReceived : 0;
        if (accept < 0 || accept > line.QuantityReceived) throw new AppException("Accepted quantity must be between 0 and the quantity received.");
        var reject = line.QuantityReceived - accept;
        if (r.Decision == LineDecision.Accept)
        {
            if (ins is not null && ins.Status != InspectionStatus.Released) throw new ConflictException($"Inspection {ins.Number} is {ins.Status}; the lot can be accepted only after it is released.");
            if (a is not null && (a.OverallResult != AqlDecision.Accept || a.Status is AqlInspectionStatus.Sampling or AqlInspectionStatus.Cancelled)) throw new ConflictException($"AQL lot {a.InspectionNumber} has not accepted the lot.");
            if (ins is null && a is null && line.InspectionRequired && string.IsNullOrWhiteSpace(r.Notes)) throw new AppException("Record the inspection result in the notes, or link the inspection.");
            if (reject > 0 && string.IsNullOrWhiteSpace(r.Notes)) throw new AppException("Explain the partial acceptance.");
        }
        if (r.Decision == LineDecision.Reject)
        {
            if (ins is not null && ins.Status is not (InspectionStatus.Failed or InspectionStatus.Rejected)) throw new ConflictException($"Inspection {ins.Number} is {ins.Status}; only a failed inspection rejects the lot.");
            if (a is not null && ins is null && a.OverallResult != AqlDecision.Reject) throw new ConflictException($"AQL lot {a.InspectionNumber} has not rejected the lot.");
            if (ins is null && a is null) ModuleSupport.Required(r.Notes, "Rejection reason", 2000);
        }
        if (r.Decision == LineDecision.Quarantine) ModuleSupport.Required(r.Notes, "Quarantine reason", 2000);
        var lot = await db.Lots.FirstAsync(x => x.Id == line.LotId, ct);
        var part = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == line.PartId, ct);
        var serials = await db.SerialUnits.Where(s => s.LotId == lot.Id).Select(s => s.Id).ToListAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        async Task MoveAsync(decimal qty, Guid to, StockStatus status, string reason)
        {
            if (qty <= 0) return;
            if (serials.Count > 0)
            {
                var avail = await db.InventoryBalances.Where(b => b.LotId == lot.Id && b.LocationId == rc.ReceivingLocationId && b.Status == StockStatus.AwaitingInspection && b.OnHand > 0 && b.SerialId != null)
                    .OrderBy(b => b.SerialId).Select(b => b.SerialId!.Value).Take((int)qty).ToListAsync(ct);
                foreach (var s in avail)
                    await inv.PostAsync(new Move(InventoryTxKind.StatusChange, part.Id, 1, lot.Id, s, rc.ReceivingLocationId, StockStatus.AwaitingInspection, ToLocationId: to, ToStatus: status, RefType: ReceiptType, RefId: rc.Id, RefNumber: rc.Number, Reason: reason), ct);
            }
            else await inv.PostAsync(new Move(InventoryTxKind.StatusChange, part.Id, qty, lot.Id, null, rc.ReceivingLocationId, StockStatus.AwaitingInspection, ToLocationId: to, ToStatus: status, RefType: ReceiptType, RefId: rc.Id, RefNumber: rc.Number, Reason: reason), ct);
        }
        if (accept > 0)
        {
            var put = r.PutAwayLocationId ?? line.PutAwayLocationId ?? throw new AppException("Select the put-away location.");
            await MoveAsync(accept, put, StockStatus.Available, "Accepted at receiving inspection");
            line.PutAwayLocationId = put;
        }
        if (reject > 0)
        {
            var q = r.QuarantineLocationId ?? await db.WarehouseLocations.Where(x => x.SiteId == rc.SiteId && x.Active && x.Purpose == LocationPurpose.Quarantine).OrderBy(x => x.Path).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
                ?? throw new AppException("Set up a quarantine location for this site.");
            var status = r.Decision == LineDecision.Quarantine ? StockStatus.Quarantined : StockStatus.Rejected;
            await MoveAsync(reject, q, status, r.Decision == LineDecision.Quarantine ? "Quarantined at receiving" : "Rejected at receiving inspection");
            Guid? ncrId = r.Decision == LineDecision.Quarantine ? null : ins?.NcrId;
            if (r.Decision == LineDecision.Reject && ncrId is null)
            {
                var ncr = ins is not null && ins.Status == InspectionStatus.Failed ? await inspections.CreateNcrAsync(ins.Id, null, ct)
                    : await ncrs.CreateAsync(new CreateNcrRequest($"Receiving rejection {part.PartNumber} lot {lot.Number}", r.Notes ?? (a is null ? "Rejected at receiving" : $"AQL lot {a.InspectionNumber} rejected: {a.Rationale}"),
                        "Receiving inspection", rc.SiteId, rc.DepartmentId, null, part.PartNumber, lot.Number, reject, rc.ReceivedOn, Severity.Major,
                        new NcrDetails(PartId: part.Id, PartRevision: line.Revision, SupplierId: rc.SupplierId, PurchaseOrder: rc.PurchaseOrderNumber, BatchNumber: line.SupplierLotNumber, DetectionPoint: "Receiving inspection",
                            Segregation: "Moved to quarantine")), ct);
                ncrId = ncr.Id;
            }
            line.NcrId = ncrId;
            if (ncrId is { } nid) db.RecordLinks.Add(new RecordLink { FromType = ReceiptType, FromId = rc.Id, ToType = NcrService.RecordType, ToId = nid, LinkType = "Nonconformance" });
            var hold = await inv.CreateHoldAsync(new HoldInput(r.HoldKind ?? (r.Decision == LineDecision.Reject && rc.SupplierId is not null ? HoldKind.Supplier : HoldKind.Quality),
                r.Notes ?? $"Receipt {rc.Number} line {line.LineNumber} {r.Decision.ToString().ToLowerInvariant()}ed", part.Id, lot.Id, null, null, ncrId, null, rc.SupplierId, rc.SiteId, rc.DepartmentId), ct, false, line.Id);
            line.HoldId = hold.Id;
        }
        line.QuantityAccepted = accept; line.QuantityRejected = reject; line.DecisionNotes = r.Notes?.Trim();
        line.Status = r.Decision switch { LineDecision.Quarantine => ReceiptLineStatus.Quarantined, _ when reject == 0 => ReceiptLineStatus.Accepted, _ when accept == 0 => ReceiptLineStatus.Rejected, _ => ReceiptLineStatus.PartiallyAccepted };
        if (lot.Status == LotStatus.Active && reject > 0 && accept == 0) lot.Status = r.Decision == LineDecision.Quarantine ? LotStatus.Quarantined : LotStatus.OnHold;
        await db.SaveChangesAsync(ct);
        await trace.RecordAsync(new TraceEventInput(TraceEventKind.Inspection, null, null, lot.Id, null, line.QuantityReceived, null, null, rc.SupplierId, null, ReceiptType, rc.Id, rc.Number,
            $"{line.Status}: {line.QuantityAccepted:0.####} accepted, {line.QuantityRejected:0.####} {(r.Decision == LineDecision.Quarantine ? "quarantined" : "rejected")}", null), ct, checkAccess: false);
        if (!await db.ReceiptLines.AnyAsync(x => x.ReceiptId == rc.Id && x.Status == ReceiptLineStatus.AwaitingInspection, ct)) rc.Status = ReceiptStatus.Completed;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>Receives a customer return (RMA) into a returns location, awaiting disposition (extends the Prompt 5 return record).</summary>
    public async Task<InventoryTransaction> ReceiveReturnStockAsync(ReturnStockInput r, CancellationToken ct)
    {
        await m.EditAsync(ReturnService.ReturnType, r.ReturnId, ct);
        var ret = await db.CustomerReturns.AsNoTracking().FirstAsync(x => x.Id == r.ReturnId, ct);
        if (ret.Status is ReturnStatus.Authorized or ReturnStatus.Cancelled or ReturnStatus.Closed) throw new ConflictException("Receive the return first.");
        var loc = await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.LocationId, ct) ?? throw new AppException("Unknown location.");
        if (loc.Purpose is not (LocationPurpose.Returns or LocationPurpose.Quarantine)) throw new AppException($"{loc.Code} is not a returns or quarantine location.");
        await m.RequireAsync(Permissions.InventoryEdit, loc.SiteId, loc.DepartmentId, ct);
        if (await db.InventoryTransactions.AnyAsync(x => x.RefType == ReturnService.ReturnType && x.RefId == ret.Id && x.Kind == InventoryTxKind.Return, ct)) throw new ConflictException("This return is already in stock.");
        var partId = ret.PartId ?? r.PartId;
        var ser = ret.SerialNumber is { } sn ? await db.SerialUnits.AsNoTracking().FirstOrDefaultAsync(x => x.SerialNumber == sn && x.PartId == partId, ct) : null;
        var lot = ser?.LotId ?? (ret.LotNumber is { } ln ? await db.Lots.Where(x => x.Number == ln && x.PartId == partId).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct) : null);
        return await inv.PostAsync(new Move(InventoryTxKind.Return, partId, ser is null ? ret.Quantity : 1, lot, ser?.Id, ToLocationId: loc.Id, ToStatus: StockStatus.AwaitingDisposition,
            RefType: ReturnService.ReturnType, RefId: ret.Id, RefNumber: ret.Number, Reason: ret.ReturnReason), ct);
    }

    // ---------------------------------------------------------------- MRB
    public async Task<MrbCase> CreateMrbAsync(MrbInput r, CancellationToken ct)
    {
        var loc = await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.LocationId, ct) ?? throw new AppException("Unknown location.");
        await m.RequireAsync(Permissions.InventoryEdit, loc.SiteId, loc.DepartmentId, ct);
        if (r.StockStatus == StockStatus.Available) throw new AppException("MRB reviews held, quarantined, rejected or returned material; put available stock on hold first.");
        if (r.Quantity <= 0) throw new AppException("Quantity must be positive.");
        var bal = await db.InventoryBalances.AsNoTracking().FirstOrDefaultAsync(b => b.PartId == r.PartId && b.LotId == r.LotId && b.SerialId == r.SerialId && b.LocationId == loc.Id && b.Status == r.StockStatus && b.WorkOrderId == null, ct);
        if (bal is null || bal.OnHand < r.Quantity) throw new ConflictException($"Only {bal?.OnHand ?? 0:0.####} is {r.StockStatus} at {loc.Code}.");
        var supplierId = r.LotId is { } l ? await db.Lots.Where(x => x.Id == l).Select(x => x.SupplierId).FirstOrDefaultAsync(ct) : null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new MrbCase { Number = await numbers.NextAsync(MrbType, loc.SiteId, ct), PartId = r.PartId, LotId = r.LotId, SerialId = r.SerialId, LocationId = loc.Id, StockStatus = StockStatus.Mrb, Quantity = r.Quantity,
            NcrId = r.NcrId, HoldId = r.HoldId, SupplierId = supplierId, Justification = r.Notes, OwnerId = m.UserId, SiteId = loc.SiteId, DepartmentId = loc.DepartmentId };
        db.MrbCases.Add(c);
        await db.SaveChangesAsync(ct);
        await inv.PostAsync(new Move(InventoryTxKind.StatusChange, r.PartId, r.Quantity, r.LotId, r.SerialId, loc.Id, r.StockStatus, ToLocationId: loc.Id, ToStatus: StockStatus.Mrb, RefType: MrbType, RefId: c.Id, RefNumber: c.Number,
            Reason: "Material review", AllowHeld: true), ct);
        if (r.NcrId is { } n) db.RecordLinks.Add(new RecordLink { FromType = MrbType, FromId = c.Id, ToType = NcrService.RecordType, ToId = n, LinkType = "Nonconformance" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(MrbType, c.Id, r.Notes, ct);
        await tx.CommitAsync(ct);
        return c;
    }

    public async Task<List<MrbCase>> MrbListAsync(MrbStatus? status, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.MrbCases.AsNoTracking(), Permissions.InventoryView, ct);
        if (status is { } s) q = q.Where(x => x.Status == s);
        return await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<MrbCase> GetMrbAsync(Guid id, CancellationToken ct) { await m.ViewAsync(MrbType, id, ct); return await db.MrbCases.AsNoTracking().FirstAsync(x => x.Id == id, ct); }

    /// <summary>Proposes the disposition; the MRB approval route (mrb.approve, e-signed) decides it and the approved disposition is executed in the same transaction.</summary>
    public async Task ProposeAsync(Guid id, MrbProposal r, CancellationToken ct)
    {
        await m.EditAsync(MrbType, id, ct);
        var c = await db.MrbCases.FirstAsync(x => x.Id == id, ct);
        if (c.Status is not (MrbStatus.Open or MrbStatus.Rejected)) throw new ConflictException($"The MRB case is {c.Status}.");
        if (r.Disposition == MrbDisposition.None) throw new AppException("Choose a disposition.");
        ModuleSupport.Required(r.Justification, "Justification", 4000);
        if (r.Disposition is MrbDisposition.Accept or MrbDisposition.UseAsIs or MrbDisposition.Rework or MrbDisposition.Repair)
        {
            var t = r.TargetLocationId is { } tl ? await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tl, ct) : null;
            if (t is null || t.SiteId != c.SiteId) throw new AppException("Select where the material goes (a location at this site).");
            if (r.Disposition is MrbDisposition.Rework or MrbDisposition.Repair && t.Purpose is not (LocationPurpose.Production or LocationPurpose.Wip)) throw new AppException("Rework and repair go to a production or WIP location.");
        }
        if (r.Disposition == MrbDisposition.ReturnToSupplier && c.SupplierId is null) throw new AppException("The material has no supplier lot; return to supplier is not possible.");
        c.Disposition = r.Disposition; c.Justification = r.Justification.Trim(); c.TargetLocationId = r.TargetLocationId; c.Status = MrbStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(MrbType, id, ct);
    }

    public async Task OnMrbRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var c = await db.MrbCases.FirstAsync(x => x.Id == id, ct);
        c.Status = MrbStatus.Rejected;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([c.OwnerId], $"MRB {c.Number} disposition rejected", comment, await m.RecordAsync(MrbType, id, ct), ct: ct);
    }

    public async Task OnMrbApprovedAsync(Guid id, CancellationToken ct)
    {
        var c = await db.MrbCases.FirstAsync(x => x.Id == id, ct);
        c.Status = MrbStatus.Approved;
        var reason = $"MRB {c.Number}: {c.Disposition}";
        switch (c.Disposition)
        {
            case MrbDisposition.Accept or MrbDisposition.UseAsIs or MrbDisposition.Rework or MrbDisposition.Repair:
                await inv.PostAsync(new Move(c.Disposition is MrbDisposition.Rework or MrbDisposition.Repair ? InventoryTxKind.Transfer : InventoryTxKind.StatusChange, c.PartId, c.Quantity, c.LotId, c.SerialId,
                    c.LocationId, StockStatus.Mrb, ToLocationId: c.TargetLocationId, ToStatus: StockStatus.Available, RefType: MrbType, RefId: c.Id, RefNumber: c.Number, Reason: reason, AllowHeld: true), ct);
                await ReleaseHoldsAsync(c, reason, ct);
                c.Status = MrbStatus.Executed; c.ExecutedAt = m.Now;
                break;
            case MrbDisposition.Scrap:
                await inv.ScrapAsync(new ScrapInput(c.PartId, c.LotId, c.SerialId, c.LocationId, StockStatus.Mrb, c.Quantity, reason, c.NcrId, null), ct, false, MrbType, c.Id, c.Number);
                await ReleaseHoldsAsync(c, reason, ct);
                c.Status = MrbStatus.Executed; c.ExecutedAt = m.Now;
                break;
            case MrbDisposition.ReturnToSupplier:
                var rtv = new SupplierReturn { Number = await numbers.NextAsync(RtvType, c.SiteId, ct), SupplierId = c.SupplierId!.Value, PartId = c.PartId, LotId = c.LotId, LocationId = c.LocationId, StockStatus = StockStatus.Mrb,
                    Quantity = c.Quantity, Reason = c.Justification ?? reason, NcrId = c.NcrId, MrbCaseId = c.Id, SiteId = c.SiteId, DepartmentId = c.DepartmentId };
                db.SupplierReturns.Add(rtv);
                c.SupplierReturnId = rtv.Id; c.Status = MrbStatus.Executed; c.ExecutedAt = m.Now;
                db.RecordLinks.Add(new RecordLink { FromType = MrbType, FromId = c.Id, ToType = RtvType, ToId = rtv.Id, LinkType = "Return to supplier" });
                if (c.NcrId is { } n) db.RecordLinks.Add(new RecordLink { FromType = RtvType, FromId = rtv.Id, ToType = NcrService.RecordType, ToId = n, LinkType = "Nonconformance" });
                break;
        }
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([c.OwnerId], $"MRB {c.Number} disposition approved ({c.Disposition})", c.Justification, await m.RecordAsync(MrbType, id, ct), email: false, ct: ct);
    }

    async Task ReleaseHoldsAsync(MrbCase c, string reason, CancellationToken ct)
    {
        var ids = await db.InventoryHolds.Where(h => (h.Id == c.HoldId || (c.LotId != null && h.LotId == c.LotId)) && (h.Status == HoldStatus.Active || h.Status == HoldStatus.ReleaseRequested)).Select(h => h.Id).ToListAsync(ct);
        // material elsewhere under the same lot hold stays held unless this case disposes all of it
        var remaining = await db.InventoryBalances.Where(b => b.LotId == c.LotId && b.OnHand > 0 && b.Status != StockStatus.Available).SumAsync(b => (decimal?)b.OnHand, ct) ?? 0;
        if (c.LotId is not null && remaining > 0 && c.Disposition != MrbDisposition.Scrap && c.HoldId is { } only) ids = [only];
        foreach (var h in ids) await inv.ReleaseAsync(h, reason, ct);
    }

    // ---------------------------------------------------------------- supplier returns (RTV)
    public async Task<List<SupplierReturn>> RtvListAsync(CancellationToken ct) => await (await m.ScopeAsync(db.SupplierReturns.AsNoTracking(), Permissions.InventoryView, ct)).OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    public async Task<SupplierReturn> GetRtvAsync(Guid id, CancellationToken ct) { await m.ViewAsync(RtvType, id, ct); return await db.SupplierReturns.AsNoTracking().FirstAsync(x => x.Id == id, ct); }

    public async Task ShipRtvAsync(Guid id, RtvShipInput r, CancellationToken ct)
    {
        await m.EditAsync(RtvType, id, ct);
        var rtv = await db.SupplierReturns.FirstAsync(x => x.Id == id, ct);
        if (rtv.Status != SupplierReturnStatus.Open) throw new ConflictException($"The supplier return is {rtv.Status}.");
        if (r.ShippedOn > await m.TodayAsync(rtv.SiteId, ct)) throw new AppException("The ship date cannot be in the future.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await inv.PostAsync(new Move(InventoryTxKind.SupplierReturn, rtv.PartId, rtv.Quantity, rtv.LotId, null, rtv.LocationId, rtv.StockStatus, RefType: RtvType, RefId: rtv.Id, RefNumber: rtv.Number,
            Reason: "Returned to supplier", AllowHeld: true), ct);
        rtv.Status = SupplierReturnStatus.Shipped; rtv.ShippedOn = r.ShippedOn; rtv.CarrierId = r.CarrierId; rtv.TrackingNumber = r.TrackingNumber?.Trim(); rtv.SupplierRma = r.SupplierRma?.Trim() ?? rtv.SupplierRma;
        db.CustodyEvents.Add(new CustodyEvent { RefType = RtvType, RefId = rtv.Id, Action = "Shipped to supplier", ById = m.UserId, At = m.Now, LocationId = rtv.LocationId, Notes = r.TrackingNumber });
        await db.SaveChangesAsync(ct);
        if (rtv.LotId is { } l && !await db.InventoryBalances.AnyAsync(b => b.LotId == l && b.OnHand > 0 && b.Status != StockStatus.Available, ct))
            foreach (var h in await db.InventoryHolds.Where(h => h.LotId == l && (h.Status == HoldStatus.Active || h.Status == HoldStatus.ReleaseRequested)).Select(h => h.Id).ToListAsync(ct))
                await inv.ReleaseAsync(h, $"Material returned to supplier ({rtv.Number})", ct);
        await tx.CommitAsync(ct);
    }

    public async Task CreditRtvAsync(Guid id, RtvCreditInput r, CancellationToken ct)
    {
        await m.EditAsync(RtvType, id, ct);
        var rtv = await db.SupplierReturns.FirstAsync(x => x.Id == id, ct);
        if (rtv.Status != SupplierReturnStatus.Shipped) throw new ConflictException("Record the credit after the material has shipped.");
        if (r.CreditAmount < 0) throw new AppException("The credit cannot be negative.");
        ModuleSupport.Required(r.CreditReference, "Credit reference", 100);
        rtv.CreditAmount = r.CreditAmount; rtv.CreditReference = r.CreditReference.Trim(); rtv.CreditedOn = r.CreditedOn; rtv.Status = SupplierReturnStatus.Credited;
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseRtvAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RtvType, id, ct);
        var rtv = await db.SupplierReturns.FirstAsync(x => x.Id == id, ct);
        if (rtv.Status is not (SupplierReturnStatus.Shipped or SupplierReturnStatus.Credited)) throw new ConflictException("Ship the return before closing it.");
        rtv.Status = SupplierReturnStatus.Closed;
        await db.SaveChangesAsync(ct);
    }
}
