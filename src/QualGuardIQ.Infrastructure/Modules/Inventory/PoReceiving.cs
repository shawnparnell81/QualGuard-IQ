using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.MasterData;
using QualGuardIQ.Infrastructure.Modules.Quality;

namespace QualGuardIQ.Infrastructure.Modules.Inventory;

public sealed record ReceivingPolicyInput(Guid? SiteId, decimal OverReceiptPercent, decimal OverReceiptQuantity, bool AllowPartialReceipt);
public sealed record ReceivingPolicyView(Guid? SiteId, decimal OverReceiptPercent, decimal OverReceiptQuantity, bool AllowPartialReceipt, bool Inherited);
public sealed record ReceivingRequirementInput(Guid PartId, Guid? SupplierId, bool InspectionRequired, Guid? InspectionPlanId, Guid? AqlPlanId, string? AqlInspectionLevel, decimal? AqlLevel,
    bool HoldOnReceipt, string? HoldReason, bool SerialsRequired, int LabelCopies, string? Notes, Guid SiteId, Guid? DepartmentId);
public sealed record ReceivingRequirementRow(Guid Id, Guid PartId, string PartNumber, Guid? SupplierId, string? SupplierName, bool InspectionRequired, Guid? InspectionPlanId, string? InspectionPlanNumber,
    Guid? AqlPlanId, string? AqlPlanName, string? AqlInspectionLevel, decimal? AqlLevel, bool HoldOnReceipt, string? HoldReason, bool SerialsRequired, int LabelCopies, bool Active, string? Notes, Guid SiteId, Guid? DepartmentId);
/// <summary>A problem with a proposed receipt line. Conflict = the request clashes with current data (HTTP 409); otherwise it is invalid input (HTTP 400).</summary>
public sealed record ReceivingIssue(string Code, string Message, bool Conflict = false);
public sealed record LineRequirement(Guid? Id, bool InspectionRequired, Guid? InspectionPlanId, string? InspectionPlanNumber, Guid? AqlPlanId, string? AqlPlanName, bool HoldOnReceipt, string? HoldReason,
    bool SerialsRequired, int LabelCopies);
public sealed record LineCheckResult(int Line, Guid? PurchaseOrderLineId, int? PoLineNumber, Guid? PartId, string? PartNumber, string? Revision, decimal? Ordered, decimal? AlreadyReceived, decimal? Remaining,
    decimal? MaxReceivable, decimal OverReceived, bool UnderReceipt, LineRequirement Requirement, List<ReceivingIssue> Errors, List<string> Warnings)
{
    public bool Ok => Errors.Count == 0;
}
public sealed record PoOpenLine(Guid Id, int LineNumber, Guid? PartId, string ItemNumber, string? PartName, string? Revision, decimal Ordered, decimal Received, decimal Remaining, decimal MaxReceivable,
    string UnitOfMeasure, DateOnly? DueOn, LineRequirement Requirement);
public sealed record PoLookup(Guid Id, string Number, Guid SupplierId, string SupplierName, string SupplierStatus, bool SupplierBlocked, List<string> SupplierConditions, string Status, Guid SiteId,
    ReceivingPolicyView Policy, List<PoOpenLine> Lines);
public sealed record ReceivingCheckInput(Guid? PurchaseOrderId, Guid SiteId, Guid? SupplierId, List<ReceiptLineInput> Lines, string? ScannedItem = null);
public sealed record ReceiptLabel(int LineNumber, string Kind, string Number, string Content, string PartNumber, string? Revision, decimal Quantity, int Copies);

/// <summary>
/// PO-based receiving rules shared by every receipt channel: over-receipt tolerance, partial receipt, part / revision / serial validation,
/// and per-part receiving requirements (inspection, AQL, hold, serials). The scan station checks a line with the same rules before posting it.
/// </summary>
public sealed partial class ReceivingService
{
    static readonly ReceivingPolicy DefaultPolicy = new() { OverReceiptPercent = 0, OverReceiptQuantity = 0, AllowPartialReceipt = true };

    public async Task<ReceivingPolicyView> PolicyAsync(Guid? siteId, CancellationToken ct)
    {
        if (siteId is { } s) await m.RequireAsync(Permissions.InventoryView, s, null, ct);
        else if (!await m.CanAnyAsync(Permissions.InventoryView, ct)) throw new ForbiddenException();
        var own = await db.ReceivingPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.SiteId == siteId, ct);
        var p = own ?? await EffectivePolicyAsync(siteId, ct);
        return new ReceivingPolicyView(siteId, p.OverReceiptPercent, p.OverReceiptQuantity, p.AllowPartialReceipt, own is null);
    }

    async Task<ReceivingPolicy> EffectivePolicyAsync(Guid? siteId, CancellationToken ct) =>
        (siteId is { } s ? await db.ReceivingPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.SiteId == s, ct) : null)
        ?? await db.ReceivingPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.SiteId == null, ct) ?? DefaultPolicy;

    /// <summary>Site policy (inventory.approve at the site) or the organization default (inventory.approve at every site the caller can see is not enough: it needs an all-site grant).</summary>
    public async Task SavePolicyAsync(ReceivingPolicyInput r, CancellationToken ct)
    {
        if (r.SiteId is { } s) { await m.ValidateLocationAsync(s, null, ct); await m.RequireAsync(Permissions.InventoryApprove, s, null, ct); }
        else if (!await m.HasAllSitesAsync(Permissions.InventoryApprove, ct)) throw new ForbiddenException("The organization default needs inventory approval rights at every site.");
        if (r.OverReceiptPercent is < 0 or > 100) throw new AppException("Over-receipt tolerance must be between 0 and 100 percent.");
        if (r.OverReceiptQuantity < 0) throw new AppException("Over-receipt quantity cannot be negative.");
        var row = await db.ReceivingPolicies.FirstOrDefaultAsync(x => x.SiteId == r.SiteId, ct);
        if (row is null) { row = new ReceivingPolicy { SiteId = r.SiteId }; db.ReceivingPolicies.Add(row); }
        row.OverReceiptPercent = r.OverReceiptPercent; row.OverReceiptQuantity = r.OverReceiptQuantity; row.AllowPartialReceipt = r.AllowPartialReceipt;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<ReceivingRequirementRow>> RequirementsAsync(Guid? partId, bool includeInactive, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.ReceivingRequirements.AsNoTracking(), Permissions.InventoryView, ct);
        if (partId is { } p) q = q.Where(x => x.PartId == p);
        if (!includeInactive) q = q.Where(x => x.Active);
        return await q.OrderBy(x => x.PartId).Take(1000)
            .Select(x => new ReceivingRequirementRow(x.Id, x.PartId, db.Parts.Where(p => p.Id == x.PartId).Select(p => p.PartNumber).First(), x.SupplierId,
                db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.LegalName).FirstOrDefault(), x.InspectionRequired, x.InspectionPlanId,
                db.InspectionPlans.Where(i => i.Id == x.InspectionPlanId).Select(i => i.Number).FirstOrDefault(), x.AqlPlanId,
                db.AqlSamplingPlans.Where(a => a.Id == x.AqlPlanId).Select(a => a.PlanName).FirstOrDefault(), x.AqlInspectionLevel, x.AqlLevel, x.HoldOnReceipt, x.HoldReason,
                x.SerialsRequired, x.LabelCopies, x.Active, x.Notes, x.SiteId, x.DepartmentId)).ToListAsync(ct);
    }

    public async Task<ReceivingRequirement> SaveRequirementAsync(Guid? id, ReceivingRequirementInput r, CancellationToken ct)
    {
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.InventoryApprove, r.SiteId, r.DepartmentId, ct);
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartId, ct) ?? throw new AppException("Select a part.");
        if (r.SupplierId is { } sid && !await db.Suppliers.AnyAsync(x => x.Id == sid, ct)) throw new AppException("Select an existing supplier.");
        if (r.InspectionPlanId is { } ip && !await db.InspectionPlans.AnyAsync(x => x.Id == ip && x.PartId == part.Id && !x.IsObsolete && x.SiteId == r.SiteId, ct))
            throw new AppException("The inspection plan must be an active plan for this part at the same site.");
        if (r.AqlPlanId is { } ap && !await db.AqlSamplingPlans.AnyAsync(x => x.Id == ap && x.Active, ct)) throw new AppException("Select an active AQL sampling plan.");
        if ((r.InspectionPlanId is not null || r.AqlPlanId is not null) && !r.InspectionRequired) throw new AppException("An inspection plan or AQL plan means receiving inspection is required.");
        if (r.AqlLevel is { } al && al is <= 0 or > 25) throw new AppException("The AQL level must be above 0 and at most 25.");
        if (r.AqlInspectionLevel is { Length: > 0 } lvl && lvl.Trim().ToUpperInvariant() is not ("I" or "II" or "III" or "S-1" or "S-2" or "S-3" or "S-4")) throw new AppException("The inspection level must be I, II, III or S-1 to S-4.");
        if (r.HoldOnReceipt) ModuleSupport.Required(r.HoldReason, "Hold reason", 500);
        if (r.LabelCopies is < 0 or > 50) throw new AppException("Label copies must be between 0 and 50.");
        ReceivingRequirement row;
        if (id is { } existing)
        {
            row = await db.ReceivingRequirements.FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException("Receiving requirement");
            await m.RequireAsync(Permissions.InventoryApprove, row.SiteId, row.DepartmentId, ct);
        }
        else { row = new ReceivingRequirement(); db.ReceivingRequirements.Add(row); }
        if (await db.ReceivingRequirements.AnyAsync(x => x.Active && x.Id != row.Id && x.PartId == r.PartId && x.SupplierId == r.SupplierId && x.SiteId == r.SiteId, ct))
            throw new ConflictException($"Part {part.PartNumber} already has an active receiving requirement for this {(r.SupplierId is null ? "site (any supplier)" : "supplier")}.");
        row.PartId = r.PartId; row.SupplierId = r.SupplierId; row.InspectionRequired = r.InspectionRequired; row.InspectionPlanId = r.InspectionPlanId; row.AqlPlanId = r.AqlPlanId;
        row.AqlInspectionLevel = string.IsNullOrWhiteSpace(r.AqlInspectionLevel) ? null : r.AqlInspectionLevel.Trim().ToUpperInvariant(); row.AqlLevel = r.AqlLevel;
        row.HoldOnReceipt = r.HoldOnReceipt; row.HoldReason = r.HoldOnReceipt ? r.HoldReason!.Trim() : null; row.SerialsRequired = r.SerialsRequired; row.LabelCopies = r.LabelCopies;
        row.Notes = r.Notes?.Trim(); row.SiteId = r.SiteId; row.DepartmentId = r.DepartmentId; row.Active = true;
        await db.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>Retires a requirement (kept for receipt lines that applied it).</summary>
    public async Task RetireRequirementAsync(Guid id, CancellationToken ct)
    {
        var row = await db.ReceivingRequirements.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Receiving requirement");
        await m.RequireAsync(Permissions.InventoryApprove, row.SiteId, row.DepartmentId, ct);
        row.Active = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The requirement for a part at a site: a supplier-specific one wins over the any-supplier one.</summary>
    async Task<ReceivingRequirement?> RequirementForAsync(Guid partId, Guid? supplierId, Guid siteId, CancellationToken ct) =>
        await db.ReceivingRequirements.AsNoTracking().Where(x => x.Active && x.PartId == partId && x.SiteId == siteId && (x.SupplierId == null || x.SupplierId == supplierId))
            .OrderByDescending(x => x.SupplierId != null).FirstOrDefaultAsync(ct);

    async Task<LineRequirement> DescribeAsync(ReceivingRequirement? q, Guid partId, Guid siteId, CancellationToken ct)
    {
        var planId = q?.InspectionPlanId ?? await DefaultPlanAsync(partId, siteId, ct);
        var planNo = planId is { } p ? await db.InspectionPlans.Where(x => x.Id == p).Select(x => x.Number).FirstOrDefaultAsync(ct) : null;
        var aqlName = q?.AqlPlanId is { } a ? await db.AqlSamplingPlans.Where(x => x.Id == a).Select(x => x.PlanName).FirstOrDefaultAsync(ct) : null;
        return new LineRequirement(q?.Id, (q?.InspectionRequired ?? false) || planId is not null, planId, planNo, q?.AqlPlanId, aqlName, q?.HoldOnReceipt ?? false, q?.HoldReason, q?.SerialsRequired ?? false, q?.LabelCopies ?? 1);
    }

    Task<Guid?> DefaultPlanAsync(Guid partId, Guid siteId, CancellationToken ct) =>
        db.InspectionPlans.Where(p => p.PartId == partId && p.InspectionType == "Receiving" && !p.IsObsolete && p.EffectiveRevisionId != null && p.SiteId == siteId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);

    static decimal Tolerance(ReceivingPolicy p, decimal ordered) => Math.Max(Math.Round(ordered * p.OverReceiptPercent / 100m, 4), p.OverReceiptQuantity);

    /// <summary>Open PO for scan-station receiving: lines with remaining and maximum receivable quantity and what receiving must do for each.</summary>
    public async Task<PoLookup> LookupPoAsync(string number, CancellationToken ct)
    {
        var v = (number ?? "").Trim(); if (v.StartsWith("QG:PO:", StringComparison.OrdinalIgnoreCase)) v = v[6..].Trim();
        if (v.Length == 0) throw new AppException("Scan or enter the PO number.");
        var q = await m.ScopeAsync(db.PurchaseOrders.AsNoTracking(), Permissions.InventoryView, ct);
        var po = await q.FirstOrDefaultAsync(x => x.Number == v, ct) ?? await q.FirstOrDefaultAsync(x => x.Number.ToUpper() == v.ToUpper(), ct) ?? throw new NotFoundException($"PO {v}");
        await m.RequireAsync(Permissions.InventoryEdit, po.SiteId, po.DepartmentId, ct);
        var sup = await db.Suppliers.AsNoTracking().FirstAsync(x => x.Id == po.SupplierId, ct);
        var check = await suppliers.CheckAsync(sup.Id, ct);
        var policy = await EffectivePolicyAsync(po.SiteId, ct);
        var lines = new List<PoOpenLine>();
        foreach (var l in await db.PurchaseOrderLines.AsNoTracking().Where(x => x.PurchaseOrderId == po.Id).OrderBy(x => x.LineNumber).ToListAsync(ct))
        {
            // a line is offered while quantity is open; the over-receipt tolerance applies to the receipt that completes it
            if (l.QuantityReceived >= l.Quantity) continue;
            var max = l.Quantity + Tolerance(policy, l.Quantity) - l.QuantityReceived;
            var part = l.PartId is { } pid ? await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct) : null;
            var req = part is null ? new LineRequirement(null, false, null, null, null, null, false, null, false, 1) : await DescribeAsync(await RequirementForAsync(part.Id, po.SupplierId, po.SiteId, ct), part.Id, po.SiteId, ct);
            lines.Add(new PoOpenLine(l.Id, l.LineNumber, l.PartId, part?.PartNumber ?? l.ItemNumber, part?.Name, l.Revision ?? part?.Revision, l.Quantity, l.QuantityReceived,
                Math.Max(0, l.Quantity - l.QuantityReceived), max, l.UnitOfMeasure, l.DueOn, req));
        }
        return new PoLookup(po.Id, po.Number, sup.Id, sup.LegalName, sup.Status.ToString(), check.Blocked, check.Conditions.Select(c => c.Message).ToList(), po.Status.ToString(), po.SiteId,
            new ReceivingPolicyView(po.SiteId, policy.OverReceiptPercent, policy.OverReceiptQuantity, policy.AllowPartialReceipt, false), lines);
    }

    /// <summary>Checks proposed lines with exactly the rules the receipt will apply, without posting anything.</summary>
    public async Task<List<LineCheckResult>> CheckAsync(ReceivingCheckInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.InventoryEdit, r.SiteId, null, ct);
        PurchaseOrder? po = null; List<PurchaseOrderLine> poLines = [];
        if (r.PurchaseOrderId is { } poId)
        {
            po = await db.PurchaseOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == poId, ct) ?? throw new NotFoundException("Purchase order");
            if (po.SiteId != r.SiteId) throw new AppException($"PO {po.Number} belongs to another site.");
            poLines = await db.PurchaseOrderLines.AsNoTracking().Where(x => x.PurchaseOrderId == poId).ToListAsync(ct);
        }
        var policy = await EffectivePolicyAsync(r.SiteId, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<LineCheckResult>();
        var n = 0;
        foreach (var li in r.Lines ?? [])
        {
            n++;
            var res = await CheckLineAsync(n, li, po, poLines, policy, r.SupplierId ?? po?.SupplierId, r.SiteId, seen, n == 1 ? r.ScannedItem : null, ct);
            if (res.PurchaseOrderLineId is { } pl && poLines.FirstOrDefault(x => x.Id == pl) is { } pol) pol.QuantityReceived += li.QuantityReceived; // later lines see the earlier ones
            results.Add(res);
        }
        return results;
    }

    /// <summary>One line against the PO, tolerance, part, revision, serial and requirement rules. Mutates nothing except the serial set.</summary>
    async Task<LineCheckResult> CheckLineAsync(int n, ReceiptLineInput li, PurchaseOrder? po, List<PurchaseOrderLine> poLines, ReceivingPolicy policy, Guid? supplierId, Guid siteId,
        HashSet<string> receiptSerials, string? scannedItem, CancellationToken ct)
    {
        var errors = new List<ReceivingIssue>(); var warnings = new List<string>();
        var none = new LineRequirement(null, false, null, null, null, null, false, null, false, 1);
        if (li.QuantityReceived <= 0) errors.Add(new("quantity", $"Line {n}: quantity received must be positive."));
        var part = await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == li.PartId, ct);
        if (part is null) { errors.Add(new("part", $"Line {n}: select a part.")); return new(n, li.PurchaseOrderLineId, null, null, null, li.Revision, null, null, null, null, 0, false, none, errors, warnings); }
        if (scannedItem is { Length: > 0 } scan)
        {
            var code = scan.Trim(); if (code.StartsWith("QG:PART:", StringComparison.OrdinalIgnoreCase)) code = code[8..].Trim();
            if (!string.Equals(code, part.PartNumber, StringComparison.OrdinalIgnoreCase)) errors.Add(new("wrong_part", $"Line {n}: scanned item {code} is not part {part.PartNumber}."));
        }
        PurchaseOrderLine? pol = null; decimal? remaining = null, max = null; decimal over = 0; var under = false;
        if (po is not null)
        {
            pol = li.PurchaseOrderLineId is { } pl ? poLines.FirstOrDefault(x => x.Id == pl) : poLines.FirstOrDefault(x => x.PartId == part.Id && x.QuantityReceived < x.Quantity);
            if (pol is null) errors.Add(new("po_line", li.PurchaseOrderLineId is null ? $"Line {n}: {part.PartNumber} is not open on PO {po.Number}." : $"Line {n}: the PO line is not on PO {po.Number}."));
            else
            {
                if (pol.PartId is { } pp ? pp != part.Id : !string.Equals(pol.ItemNumber.Trim(), part.PartNumber, StringComparison.OrdinalIgnoreCase))
                    errors.Add(new("wrong_part", $"Line {n}: PO line {pol.LineNumber} is for {pol.ItemNumber}, not {part.PartNumber}."));
                remaining = Math.Max(0, pol.Quantity - pol.QuantityReceived);
                max = pol.Quantity + Tolerance(policy, pol.Quantity) - pol.QuantityReceived;
                if (remaining == 0) errors.Add(new("line_complete", $"Line {n}: PO line {pol.LineNumber} is fully received.", true));
                else if (li.QuantityReceived > max)
                    errors.Add(new("over_receipt", max <= remaining
                        ? $"Line {n}: receiving {li.QuantityReceived:0.####} would exceed PO line {pol.LineNumber} ({remaining:0.####} open)."
                        : $"Line {n}: receiving {li.QuantityReceived:0.####} would exceed PO line {pol.LineNumber} ({remaining:0.####} open, {max:0.####} with the over-receipt tolerance).", true));
                else if (li.QuantityReceived > remaining) { over = li.QuantityReceived - remaining.Value; warnings.Add($"Line {n}: {over:0.####} over the ordered quantity, within the over-receipt tolerance."); }
                else if (li.QuantityReceived < remaining)
                {
                    under = true;
                    if (!policy.AllowPartialReceipt) errors.Add(new("partial_not_allowed", $"Line {n}: this site receives PO lines in full; {remaining:0.####} are open."));
                    else warnings.Add($"Line {n}: partial receipt; {remaining - li.QuantityReceived:0.####} stay open on PO line {pol.LineNumber}.");
                }
            }
        }
        var revision = li.Revision?.Trim() is { Length: > 0 } given ? given : pol?.Revision ?? part.Revision;
        if (li.Revision?.Trim() is { Length: > 0 } lr)
        {
            var known = string.Equals(part.Revision, lr, StringComparison.OrdinalIgnoreCase) || await db.PartRevisions.AnyAsync(x => x.PartId == part.Id && x.Revision == lr, ct);
            if (!known) errors.Add(new("unknown_revision", $"Line {n}: revision {lr} is not a revision of part {part.PartNumber}."));
            else if (pol?.Revision is { } pr && !string.Equals(pr, lr, StringComparison.OrdinalIgnoreCase)) errors.Add(new("wrong_revision", $"Line {n}: PO line {pol.LineNumber} orders revision {pr}, revision {lr} was delivered."));
        }
        var req = await RequirementForAsync(part.Id, supplierId, siteId, ct);
        var view = await DescribeAsync(req, part.Id, siteId, ct);
        var raw = li.SerialNumbers?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList() ?? [];
        var dupInScan = raw.GroupBy(s => s, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupInScan.Count > 0) errors.Add(new("duplicate_serial", $"Line {n}: serial {string.Join(", ", dupInScan)} was scanned more than once."));
        var distinct = raw.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var dupInReceipt = distinct.Where(s => !receiptSerials.Add(part.Id + "|" + s)).ToList();
        if (dupInReceipt.Count > 0) errors.Add(new("duplicate_serial", $"Line {n}: serial {string.Join(", ", dupInReceipt)} is already on another line of this receipt."));
        if (distinct.Count > 0)
        {
            var upper = distinct.Select(s => s.ToUpperInvariant()).ToList();
            var existing = await db.SerialUnits.AsNoTracking().Where(x => x.PartId == part.Id && upper.Contains(x.SerialNumber.ToUpper())).Select(x => x.SerialNumber).Take(20).ToListAsync(ct);
            if (existing.Count > 0) errors.Add(new("duplicate_serial", $"Line {n}: serial {string.Join(", ", existing)} already exists for part {part.PartNumber}.", true));
            if (raw.Count != li.QuantityReceived) errors.Add(new("serial_count", $"Line {n}: {li.QuantityReceived:0.####} units need {li.QuantityReceived:0.####} serial numbers ({raw.Count} entered)."));
        }
        else if (view.SerialsRequired) errors.Add(new("serials_required", $"Line {n}: part {part.PartNumber} is received with a serial number per unit."));
        if (req is { InspectionRequired: true } && li.InspectionRequired == false) errors.Add(new("inspection_required", $"Line {n}: part {part.PartNumber} requires receiving inspection{(view.InspectionPlanNumber is { } pn ? $" (plan {pn})" : "")}; it cannot be skipped."));
        if (li.InspectionPlanId is { } ip && !await db.InspectionPlans.AnyAsync(x => x.Id == ip && x.PartId == part.Id && !x.IsObsolete, ct)) errors.Add(new("inspection_plan", $"Line {n}: the inspection plan is not an active plan for {part.PartNumber}."));
        if (li.PlaceHold == true && string.IsNullOrWhiteSpace(li.HoldReason)) errors.Add(new("hold_reason", $"Line {n}: give the reason for the receiving hold."));
        return new(n, pol?.Id, pol?.LineNumber, part.Id, part.PartNumber, revision, pol?.Quantity, pol?.QuantityReceived, remaining, max, over, under, view, errors, warnings);
    }

    /// <summary>Lot (and serial) labels for a receipt, with the copies the receiving requirement asks for. Printing is routed by the label service.</summary>
    public async Task<List<ReceiptLabel>> LabelsAsync(Guid receiptId, CancellationToken ct)
    {
        await m.ViewAsync(ReceiptType, receiptId, ct);
        var lines = await db.ReceiptLines.AsNoTracking().Where(x => x.ReceiptId == receiptId).OrderBy(x => x.LineNumber).ToListAsync(ct);
        var labels = new List<ReceiptLabel>();
        foreach (var l in lines)
        {
            var pn = await db.Parts.Where(x => x.Id == l.PartId).Select(x => x.PartNumber).FirstAsync(ct);
            var copies = l.ReceivingRequirementId is { } rq ? await db.ReceivingRequirements.Where(x => x.Id == rq).Select(x => x.LabelCopies).FirstAsync(ct) : 1;
            if (l.LotId is { } lid && await db.Lots.Where(x => x.Id == lid).Select(x => x.Number).FirstOrDefaultAsync(ct) is { } lot)
                labels.Add(new ReceiptLabel(l.LineNumber, "Lot", lot, "QG:LOT:" + lot, pn, l.Revision, l.QuantityReceived, copies));
            foreach (var sn in (l.SerialNumbers ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                labels.Add(new ReceiptLabel(l.LineNumber, "Serial", sn, $"QG:SN:{pn}:{sn}", pn, l.Revision, 1, copies));
        }
        return labels;
    }
}
