using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Inventory;
using QualGuardIQ.Infrastructure.Modules.Logistics;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Warehouse locations, stock, the inventory ledger, holds, issues, transfers, counts and ERP mirror mode.</summary>
[ApiController, Authorize, Route("api/warehouse")]
public sealed class WarehouseController(InventoryService inv, CountService counts, WarehouseInsights insights, LabelPrintService labels) : ControllerBase
{
    [HttpGet("locations")] [RequirePermission(Permissions.InventoryView)] public Task<List<LocationRow>> Locations([FromQuery] Guid? siteId, CancellationToken ct) => inv.LocationsAsync(siteId, ct);
    [HttpPost("locations")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> CreateLocation(LocationInput r, CancellationToken ct) { var l = await inv.CreateLocationAsync(r, ct); return new { l.Id, l.Code, l.Path }; }
    [HttpGet("locations/{id:guid}")] public Task<WarehouseLocation> Location(Guid id, CancellationToken ct) => inv.GetLocationAsync(id, ct);
    [HttpPut("locations/{id:guid}")] public async Task<IActionResult> UpdateLocation(Guid id, LocationUpdate r, CancellationToken ct) { await inv.UpdateLocationAsync(id, r, ct); return NoContent(); }

    /// <summary>Stock balances (derived from the ledger). includeChildren with locationId covers the whole location subtree.</summary>
    [HttpGet("stock")] [RequirePermission(Permissions.InventoryView)]
    public Task<List<StockRow>> Stock([FromQuery] Guid? siteId, [FromQuery] Guid? partId, [FromQuery] Guid? lotId, [FromQuery] Guid? serialId, [FromQuery] Guid? locationId, [FromQuery] StockStatus? status,
        [FromQuery] Guid? workOrderId, [FromQuery] string? q, [FromQuery] bool includeChildren, CancellationToken ct) => inv.StockAsync(siteId, partId, lotId, serialId, locationId, status, workOrderId, q, includeChildren, ct);
    [HttpGet("summary")] [RequirePermission(Permissions.InventoryView)] public Task<List<PartSummary>> Summary([FromQuery] Guid? siteId, [FromQuery] string? q, CancellationToken ct) => inv.SummaryAsync(siteId, q, ct);
    /// <summary>Immutable inventory transaction history.</summary>
    [HttpGet("ledger")] [RequirePermission(Permissions.InventoryView)]
    public Task<List<TxRow>> Ledger([FromQuery] Guid? siteId, [FromQuery] Guid? partId, [FromQuery] Guid? lotId, [FromQuery] Guid? serialId, [FromQuery] Guid? locationId, [FromQuery] InventoryTxKind? kind,
        [FromQuery] Guid? workOrderId, [FromQuery] string? refType, [FromQuery] Guid? refId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct) =>
        inv.TransactionsAsync(siteId, partId, lotId, serialId, locationId, kind, workOrderId, refType, refId, from, to, ct);
    /// <summary>Whether the site's inventory is a read-only mirror of an ERP.</summary>
    [HttpGet("sites/{siteId:guid}/mode")] [RequirePermission(Permissions.InventoryView)] public Task<SiteMode> Mode(Guid siteId, CancellationToken ct) => inv.SiteModeAsync(siteId, ct);
    [HttpPost("mirror")] [RequirePermission(Permissions.IntegrationsEdit)] public Task<MirrorResult> Mirror(MirrorInput r, CancellationToken ct) => inv.ApplyMirrorAsync(r, ct);

    [HttpPost("moves")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> Move(MoveInput r, CancellationToken ct) => Tx(await inv.MoveAsync(r, ct));
    /// <summary>Scan to move: item, source location and destination location codes.</summary>
    [HttpPost("moves/scan")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> MoveScan(ScanMoveInput r, CancellationToken ct) => Tx(await inv.MoveByScanAsync(r, ct));
    [HttpPost("adjustments")] [RequirePermission(Permissions.InventoryApprove)] public async Task<object> Adjust(AdjustInput r, CancellationToken ct) => Tx(await inv.AdjustAsync(r, ct));
    [HttpPost("scrap")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> Scrap(ScrapInput r, CancellationToken ct) { var t = await inv.ScrapAsync(r, ct); await this.AutoAsync(labels, LabelType.Scrap, InventoryService.TxType, t.Id, ct); return Tx(t); }
    [HttpPost("reservations")] [RequirePermission(Permissions.InventoryEdit)] public async Task<IActionResult> Reserve(ReserveInput r, CancellationToken ct) { await inv.ReserveAsync(r, ct); return NoContent(); }

    [HttpGet("holds")] [RequirePermission(Permissions.InventoryView)] public Task<List<InventoryHold>> Holds([FromQuery] HoldStatus? status, [FromQuery] Guid? lotId, CancellationToken ct) => inv.HoldsAsync(status, lotId, ct);
    [HttpPost("holds")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> CreateHold(HoldInput r, CancellationToken ct) { var h = await inv.CreateHoldAsync(r, ct); await this.AutoAsync(labels, LabelType.Hold, InventoryService.HoldType, h.Id, ct); return new { h.Id, h.Number }; }
    [HttpGet("holds/{id:guid}")] public Task<InventoryHold> Hold(Guid id, CancellationToken ct) => inv.GetHoldAsync(id, ct);
    /// <summary>Requests release; the hold stays until the approval route (inventory.approve, e-signed) signs it off.</summary>
    [HttpPost("holds/{id:guid}/request-release")] public async Task<IActionResult> RequestRelease(Guid id, NotesInput r, CancellationToken ct) { await inv.RequestReleaseAsync(id, r, ct); return NoContent(); }

    [HttpPost("issues")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> Issue(IssueInput r, CancellationToken ct) => Tx(await inv.IssueAsync(r, ct));
    /// <summary>Scan to issue: work order, material and source location codes.</summary>
    [HttpPost("issues/scan")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> IssueScan(ScanIssueInput r, CancellationToken ct) => Tx(await inv.IssueByScanAsync(r, ct));
    [HttpPost("consumptions")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> Consume(ConsumeInput r, CancellationToken ct) => Tx(await inv.ConsumeAsync(r, ct));
    [HttpPost("completions")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> Complete(CompleteInput r, CancellationToken ct) { var l = await inv.CompleteAsync(r, ct); return new { l.Id, l.Number }; }

    /// <summary>Splits part of a lot into a new child lot (ledger + genealogy; holds follow the material).</summary>
    [HttpPost("lots/split")] [RequirePermission(Permissions.InventoryEdit)] public Task<LotResult> Split(SplitInput r, CancellationToken ct) => inv.SplitLotAsync(r, ct);
    /// <summary>Merges whole lots of one part and revision into a target lot.</summary>
    [HttpPost("lots/merge")] [RequirePermission(Permissions.InventoryEdit)] public Task<LotResult> Merge(MergeInput r, CancellationToken ct) => inv.MergeLotsAsync(r, ct);
    /// <summary>Work in process by work order, operation, location and status.</summary>
    [HttpGet("wip")] [RequirePermission(Permissions.InventoryView)] public Task<List<WipRow>> Wip([FromQuery] Guid? siteId, [FromQuery] Guid? workOrderId, CancellationToken ct) => inv.WipAsync(siteId, workOrderId, ct);

    [HttpGet("transfers")] [RequirePermission(Permissions.InventoryView)] public Task<List<StockTransfer>> Transfers([FromQuery] TransferStatus? status, CancellationToken ct) => inv.TransfersAsync(status, ct);
    [HttpPost("transfers")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> CreateTransfer(TransferInput r, CancellationToken ct) { var t = await inv.CreateTransferAsync(r, ct); return new { t.Id, t.Number }; }
    [HttpGet("transfers/{id:guid}")] public Task<StockTransfer> Transfer(Guid id, CancellationToken ct) => inv.GetTransferAsync(id, ct);
    [HttpPost("transfers/{id:guid}/ship")] public async Task<IActionResult> ShipTransfer(Guid id, TransferShipInput r, CancellationToken ct) { await inv.ShipTransferAsync(id, r, ct); return NoContent(); }
    [HttpPost("transfers/{id:guid}/receive")] public async Task<IActionResult> ReceiveTransfer(Guid id, NotesInput? r, CancellationToken ct) { await inv.ReceiveTransferAsync(id, r, ct); return NoContent(); }
    [HttpGet("transfers/{id:guid}/custody")] public Task<List<CustodyRow>> TransferCustody(Guid id, CancellationToken ct) => inv.CustodyAsync(InventoryService.TransferType, id, ct);
    [HttpGet("transfers/{id:guid}/document")] public async Task<FileContentResult> TransferDocument(Guid id, CancellationToken ct) => File(await inv.TransferDocumentAsync(id, ct), "application/pdf", "transfer.pdf");

    [HttpGet("counts")] [RequirePermission(Permissions.InventoryView)] public Task<List<CycleCount>> Counts(CancellationToken ct) => counts.ListAsync(ct);
    [HttpPost("counts")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> CreateCount(CountInput r, CancellationToken ct) { var c = await counts.CreateAsync(r, ct); return new { c.Id, c.Number }; }
    /// <summary>Count sheet and variance report.</summary>
    [HttpGet("counts/{id:guid}")] public Task<CountDetail> Count(Guid id, CancellationToken ct) => counts.GetAsync(id, ct);
    [HttpPost("counts/{id:guid}/entries")] public async Task<IActionResult> CountEntries(Guid id, CountEntries r, CancellationToken ct) { await counts.RecordAsync(id, r, ct); return NoContent(); }
    [HttpPost("counts/{id:guid}/submit")] public async Task<IActionResult> SubmitCount(Guid id, CancellationToken ct) { await counts.SubmitAsync(id, ct); return NoContent(); }
    [HttpPost("counts/{id:guid}/cancel")] public async Task<IActionResult> CancelCount(Guid id, CancellationToken ct) { await counts.CancelAsync(id, ct); return NoContent(); }

    [HttpGet("dashboard")] [RequirePermission(Permissions.InventoryView)] public Task<WarehouseDashboard> Dashboard([FromQuery] Guid? siteId, CancellationToken ct) => insights.DashboardAsync(siteId, ct);
    /// <summary>Scan context for the mobile flows (receive, inspect, move, issue, ship, trace).</summary>
    [HttpPost("scan")] public Task<ScanContext> Scan(ScanRequest r, CancellationToken ct) => insights.ScanAsync(r.Code, ct);

    static object Tx(InventoryTransaction t) => new { t.Id, t.Number, t.Kind, t.Quantity };
}

/// <summary>Receiving, receiving inspection outcome, MRB and returns to vendor.</summary>
[ApiController, Authorize, Route("api/warehouse")]
public sealed class ReceivingController(ReceivingService svc, InventoryService inv, LabelPrintService labels, QualGuardIQ.Infrastructure.Persistence.AppDbContext db) : ControllerBase
{
    [HttpGet("receipts")] [RequirePermission(Permissions.InventoryView)] public Task<List<Receipt>> Receipts([FromQuery] ReceiptStatus? status, CancellationToken ct) => svc.ListAsync(status, ct);
    [HttpPost("receipts")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> CreateReceipt(ReceiptInput r, CancellationToken ct) { var x = await svc.CreateAsync(r, ct); await this.AutoAsync(labels, LabelType.Receiving, ReceivingService.ReceiptType, x.Id, ct); return new { x.Id, x.Number, x.Status }; }
    [HttpGet("receipts/{id:guid}")] public Task<ReceiptDetail> Receipt(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    /// <summary>Lot and serial labels for a receipt (copies from the receiving requirement).</summary>
    [HttpGet("receipts/{id:guid}/labels")] public Task<List<ReceiptLabel>> ReceiptLabels(Guid id, CancellationToken ct) => svc.LabelsAsync(id, ct);
    /// <summary>Open PO for receiving by number (or a scanned QG:PO: label): open lines, remaining and maximum receivable quantity, receiving requirements.</summary>
    [HttpGet("receiving/po")] [RequirePermission(Permissions.InventoryEdit)] public Task<PoLookup> LookupPo([FromQuery] string number, CancellationToken ct) => svc.LookupPoAsync(number, ct);
    /// <summary>Checks proposed receipt lines with the rules the receipt applies (part, revision, tolerance, serials, requirements) without posting.</summary>
    [HttpPost("receiving/check")] [RequirePermission(Permissions.InventoryEdit)] public Task<List<LineCheckResult>> CheckReceipt(ReceivingCheckInput r, CancellationToken ct) => svc.CheckAsync(r, ct);
    [HttpGet("receiving/policy")] [RequirePermission(Permissions.InventoryView)] public Task<ReceivingPolicyView> Policy([FromQuery] Guid? siteId, CancellationToken ct) => svc.PolicyAsync(siteId, ct);
    [HttpPut("receiving/policy")] [RequirePermission(Permissions.InventoryApprove)] public async Task<IActionResult> SavePolicy(ReceivingPolicyInput r, CancellationToken ct) { await svc.SavePolicyAsync(r, ct); return NoContent(); }
    [HttpGet("receiving/requirements")] [RequirePermission(Permissions.InventoryView)] public Task<List<ReceivingRequirementRow>> Requirements([FromQuery] Guid? partId, [FromQuery] bool includeInactive, CancellationToken ct) => svc.RequirementsAsync(partId, includeInactive, ct);
    [HttpPost("receiving/requirements")] [RequirePermission(Permissions.InventoryApprove)] public async Task<object> CreateRequirement(ReceivingRequirementInput r, CancellationToken ct) => new { (await svc.SaveRequirementAsync(null, r, ct)).Id };
    [HttpPut("receiving/requirements/{id:guid}")] [RequirePermission(Permissions.InventoryApprove)] public async Task<IActionResult> UpdateRequirement(Guid id, ReceivingRequirementInput r, CancellationToken ct) { await svc.SaveRequirementAsync(id, r, ct); return NoContent(); }
    [HttpPost("receiving/requirements/{id:guid}/retire")] [RequirePermission(Permissions.InventoryApprove)] public async Task<IActionResult> RetireRequirement(Guid id, CancellationToken ct) { await svc.RetireRequirementAsync(id, ct); return NoContent(); }
    [HttpPost("receipts/{id:guid}/lines/{lineId:guid}/inspection")] public async Task<IActionResult> LinkInspection(Guid id, Guid lineId, LinkInspectionInput r, CancellationToken ct) { await svc.LinkInspectionAsync(id, lineId, r, ct); return NoContent(); }
    /// <summary>Inspection outcome: Accept, Reject (quarantine + hold + NCR) or Quarantine (hold).</summary>
    [HttpPost("receipts/{id:guid}/lines/{lineId:guid}/decision")] public async Task<IActionResult> Decide(Guid id, Guid lineId, LineDecisionInput r, CancellationToken ct)
    {
        var before = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.ReceiptLines.Where(x => x.Id == lineId).Select(x => x.HoldId), ct);
        await svc.DecideLineAsync(id, lineId, r, ct);
        // a rejected or quarantined line gets a quarantine label for the hold it created
        var after = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.ReceiptLines.Where(x => x.Id == lineId).Select(x => x.HoldId), ct);
        if (after is { } hold && hold != before) await this.AutoAsync(labels, LabelType.Quarantine, InventoryService.HoldType, hold, ct);
        return NoContent();
    }
    /// <summary>Puts a received customer return (RMA) into a returns location awaiting disposition.</summary>
    [HttpPost("returns/receive")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> ReceiveReturn(ReturnStockInput r, CancellationToken ct) { var t = await svc.ReceiveReturnStockAsync(r, ct); return new { t.Id, t.Number }; }

    [HttpGet("mrb")] [RequirePermission(Permissions.InventoryView)] public Task<List<MrbCase>> Mrb([FromQuery] MrbStatus? status, CancellationToken ct) => svc.MrbListAsync(status, ct);
    [HttpPost("mrb")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> CreateMrb(MrbInput r, CancellationToken ct) { var c = await svc.CreateMrbAsync(r, ct); return new { c.Id, c.Number }; }
    [HttpGet("mrb/{id:guid}")] public Task<MrbCase> MrbCase(Guid id, CancellationToken ct) => svc.GetMrbAsync(id, ct);
    /// <summary>Proposes the disposition (Accept, Rework, Repair, Scrap, ReturnToSupplier, UseAsIs); mrb.approve signs it and it is executed.</summary>
    [HttpPost("mrb/{id:guid}/propose")] public async Task<IActionResult> Propose(Guid id, MrbProposal r, CancellationToken ct) { await svc.ProposeAsync(id, r, ct); return NoContent(); }

    [HttpGet("supplier-returns")] [RequirePermission(Permissions.InventoryView)] public Task<List<SupplierReturn>> Rtvs(CancellationToken ct) => svc.RtvListAsync(ct);
    [HttpGet("supplier-returns/{id:guid}")] public Task<SupplierReturn> Rtv(Guid id, CancellationToken ct) => svc.GetRtvAsync(id, ct);
    [HttpPost("supplier-returns/{id:guid}/ship")] public async Task<IActionResult> ShipRtv(Guid id, RtvShipInput r, CancellationToken ct) { await svc.ShipRtvAsync(id, r, ct); return NoContent(); }
    [HttpPost("supplier-returns/{id:guid}/credit")] public async Task<IActionResult> CreditRtv(Guid id, RtvCreditInput r, CancellationToken ct) { await svc.CreditRtvAsync(id, r, ct); return NoContent(); }
    [HttpPost("supplier-returns/{id:guid}/close")] public async Task<IActionResult> CloseRtv(Guid id, CancellationToken ct) { await svc.CloseRtvAsync(id, ct); return NoContent(); }
    [HttpGet("supplier-returns/{id:guid}/custody")] [RequirePermission(Permissions.InventoryView)] public Task<List<CustodyRow>> RtvCustody(Guid id, CancellationToken ct) => inv.CustodyAsync(ReceivingService.RtvType, id, ct);
}

/// <summary>Shipments: pick, pack, verify, ship; carriers; packing list and bill of lading.</summary>
[ApiController, Authorize, Route("api/shipping")]
public sealed class ShippingController(ShippingService svc, InventoryService inv, LabelPrintService labels, CarrierIntegrationService carriers, ShippingDocumentService docs) : ControllerBase
{
    [HttpGet("carriers")] [RequirePermission(Permissions.ShippingView)] public Task<List<Carrier>> Carriers(CancellationToken ct) => svc.CarriersAsync(ct);
    [HttpPost("carriers")] [RequirePermission(Permissions.ShippingEdit)] public Task<Carrier> CreateCarrier(CarrierInput r, CancellationToken ct) => svc.SaveCarrierAsync(null, r, ct);
    [HttpPut("carriers/{id:guid}")] [RequirePermission(Permissions.ShippingEdit)] public Task<Carrier> UpdateCarrier(Guid id, CarrierInput r, CancellationToken ct) => svc.SaveCarrierAsync(id, r, ct);

    [HttpGet("shipments")] [RequirePermission(Permissions.ShippingView)] public Task<List<Shipment>> Shipments([FromQuery] ShipmentStatus? status, CancellationToken ct) => svc.ListAsync(status, ct);
    [HttpPost("shipments")] [RequirePermission(Permissions.ShippingEdit)] public async Task<object> Create(ShipmentInput r, CancellationToken ct) { var s = await svc.CreateAsync(r, ct); return new { s.Id, s.Number }; }
    [HttpGet("shipments/{id:guid}")] public Task<ShipmentDetail> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    /// <summary>Allocates available, unheld stock and builds the pick list.</summary>
    [HttpPost("shipments/{id:guid}/pick-list")] public Task<List<PickTask>> PickList(Guid id, CancellationToken ct) => svc.BuildPickListAsync(id, ct);
    [HttpPost("shipments/{id:guid}/picks/{taskId:guid}")] public async Task<IActionResult> Pick(Guid id, Guid taskId, PickConfirmInput r, CancellationToken ct) { await svc.ConfirmPickAsync(id, taskId, r, ct); return NoContent(); }
    /// <summary>Scan-station / device pick confirmation: idempotent on clientRequestId, records the device time, returns the outcome (Accepted or Duplicate). Conflicts return 409 with the reason.</summary>
    [HttpPost("shipments/{id:guid}/picks/{taskId:guid}/confirm")] public Task<PickConfirmResult> ConfirmPick(Guid id, Guid taskId, PickConfirmInput r, CancellationToken ct) => svc.ConfirmPickAsync(id, taskId, r, ct);
    /// <summary>Every pick confirmation received for the shipment (accepted, duplicate replays excluded, refused with their conflict code).</summary>
    [HttpGet("shipments/{id:guid}/pick-confirmations")] public Task<List<PickConfirmationRow>> PickConfirmations(Guid id, CancellationToken ct) => svc.PickConfirmationsAsync(id, ct);
    [HttpGet("shipments/{id:guid}/pickers")] public Task<List<PickerOption>> Pickers(Guid id, CancellationToken ct) => svc.PickersAsync(id, ct);
    [HttpPut("shipments/{id:guid}/picker")] public async Task<object> AssignPicker(Guid id, AssignPickerInput r, CancellationToken ct) { var s = await svc.AssignPickerAsync(id, r, ct); return new { s.Id, s.AssignedPickerId, s.AssignedAt }; }
    /// <summary>The caller's assigned open pick work, for download to a scan device.</summary>
    [HttpGet("my-picks")] [RequirePermission(Permissions.ShippingEdit)] public Task<MyPicks> MyPicks(CancellationToken ct) => svc.MyPicksAsync(ct);
    [HttpPost("shipments/{id:guid}/containers")] public async Task<object> Pack(Guid id, PackInput r, CancellationToken ct) { var c = await svc.PackAsync(id, r, ct); await this.AutoAsync(labels, c.Kind == ContainerKind.Pallet ? LabelType.Pallet : LabelType.Carton, ShippingService.ContainerType, c.Id, ct); return new { c.Id, c.Number }; }
    [HttpPost("shipments/{id:guid}/verify")] public Task<VerifyResult> Verify(Guid id, VerifyInput r, CancellationToken ct) => svc.VerifyAsync(id, r, ct);
    [HttpPost("shipments/{id:guid}/ship")] public async Task<IActionResult> Ship(Guid id, ShipInput r, CancellationToken ct) { await svc.ShipAsync(id, r, ct); await this.AutoAsync(labels, LabelType.Shipping, ShippingService.ShipmentType, id, ct); return NoContent(); }
    [HttpPost("shipments/{id:guid}/delivered")] public async Task<IActionResult> Delivered(Guid id, DeliveredInput r, CancellationToken ct) { await svc.DeliveredAsync(id, r, ct); return NoContent(); }
    /// <summary>Cancels before shipping; staged (picked) stock needs returnLocationId and is moved back as available stock.</summary>
    [HttpPost("shipments/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CancelShipmentInput? r, CancellationToken ct) { await svc.CancelAsync(id, r, ct); await carriers.VoidOnCancelAsync(id, r?.Reason, ct); return NoContent(); }
    [HttpPut("shipments/{id:guid}/verifier")] public async Task<object> AssignVerifier(Guid id, AssignVerifierInput r, CancellationToken ct) { var s = await svc.AssignVerifierAsync(id, r, ct); return new { s.Id, s.AssignedVerifierId }; }
    /// <summary>The caller's assigned shipments awaiting verification, for download to a scan device.</summary>
    [HttpGet("my-verifications")] [RequirePermission(Permissions.ShippingEdit)] public Task<MyVerifications> MyVerifications(CancellationToken ct) => svc.MyVerificationsAsync(ct);
    [HttpGet("shipments/{id:guid}/verification")] public Task<VerificationStatus> Verification(Guid id, CancellationToken ct) => svc.VerificationAsync(id, ct);
    /// <summary>One verification scan (desk or device queue): idempotent on clientRequestId; refused scans return 409 with the reason.</summary>
    [HttpPost("shipments/{id:guid}/verification/scans")] public Task<VerificationStatus> VerificationScan(Guid id, VerificationScanInput r, CancellationToken ct) => svc.ScanVerificationAsync(id, r, ct);
    [HttpGet("shipments/{id:guid}/verification/scans")] public Task<List<VerificationScanRow>> VerificationScans(Guid id, CancellationToken ct) => svc.VerificationScansAsync(id, ct);
    /// <summary>Confirms online that inventory, holds, revision, customer requirements and status allow release (required after offline scans).</summary>
    [HttpPost("shipments/{id:guid}/verification/validate")] public Task<VerificationStatus> ValidateVerification(Guid id, CancellationToken ct) => svc.ValidateVerificationAsync(id, ct);
    [HttpPost("shipments/{id:guid}/verification/restart")] public Task<VerificationStatus> RestartVerification(Guid id, CancellationToken ct) => svc.RestartVerificationAsync(id, ct);
    /// <summary>Issues the packing list from the template that applies (or the built-in layout) and records it; X-Document-Id names the issued document.</summary>
    [HttpGet("shipments/{id:guid}/packing-list")] public Task<FileContentResult> PackingList(Guid id, CancellationToken ct) => Issue(id, ShippingDocumentKind.PackingList, "packing-list.pdf", ct);
    /// <summary>Issues the bill of lading (after shipping) from the template that applies and records it.</summary>
    [HttpGet("shipments/{id:guid}/bill-of-lading")] public Task<FileContentResult> Bol(Guid id, CancellationToken ct) => Issue(id, ShippingDocumentKind.BillOfLading, "bill-of-lading.pdf", ct);
    async Task<FileContentResult> Issue(Guid id, ShippingDocumentKind kind, string name, CancellationToken ct)
    {
        var (docId, pdf) = await docs.GenerateAsync(id, kind, ct);
        Response.Headers["X-Document-Id"] = docId.ToString();
        return File(pdf, "application/pdf", name);
    }
    /// <summary>Freight terms and special instructions printed on the shipping documents (until the shipment ships).</summary>
    [HttpPut("shipments/{id:guid}/shipping-terms")] public async Task<IActionResult> Terms(Guid id, ShippingTermsInput r, CancellationToken ct) { await docs.SaveTermsAsync(id, r, ct); return NoContent(); }
    /// <summary>Which template each document of this shipment would use now.</summary>
    [HttpGet("shipments/{id:guid}/document-templates")] public Task<List<TemplateChoice>> DocumentChoices(Guid id, CancellationToken ct) => docs.ChoicesAsync(id, ct);
    /// <summary>Documents issued for this shipment, newest first, with the template revision used.</summary>
    [HttpGet("shipments/{id:guid}/documents")] public Task<List<GeneratedRow>> Documents(Guid id, CancellationToken ct) => docs.IssuedListAsync(id, null, null, ct);
    [HttpGet("shipments/{id:guid}/custody")] public Task<List<CustodyRow>> Custody(Guid id, CancellationToken ct) => inv.CustodyAsync(ShippingService.ShipmentType, id, ct);
}

/// <summary>Carrier connections (sandbox by default, disabled by default) and the carrier operations on a shipment: address validation, rates, service selection, labels, void and tracking.</summary>
[ApiController, Authorize, Route("api/shipping")]
public sealed class CarrierIntegrationController(CarrierIntegrationService svc) : ControllerBase
{
    /// <summary>Adapters with their capability metadata and unverified assumptions.</summary>
    [HttpGet("carrier-adapters")] [RequirePermission(Permissions.ShippingView)] public List<CarrierCapabilities> Adapters() => svc.Catalog();
    [HttpGet("carrier-connections")] public Task<List<CarrierConnectionRow>> Connections([FromQuery] Guid? siteId, CancellationToken ct) => svc.ConnectionsAsync(siteId, ct);
    [HttpGet("carrier-connections/{id:guid}")] public Task<CarrierConnectionRow> Connection(Guid id, CancellationToken ct) => svc.ConnectionAsync(id, ct);
    [HttpPost("carrier-connections")] [RequirePermission(Permissions.IntegrationsEdit)] public async Task<CarrierConnectionRow> Create(CarrierConnectionInput r, CancellationToken ct) => await svc.ConnectionAsync((await svc.SaveConnectionAsync(null, r, ct)).Id, ct);
    [HttpPut("carrier-connections/{id:guid}")] [RequirePermission(Permissions.IntegrationsEdit)] public async Task<CarrierConnectionRow> Update(Guid id, CarrierConnectionInput r, CancellationToken ct) { await svc.SaveConnectionAsync(id, r, ct); return await svc.ConnectionAsync(id, ct); }
    /// <summary>Connection test for the current configuration; the result is recorded and a failure switches the connection off.</summary>
    [HttpPost("carrier-connections/{id:guid}/test")] [RequirePermission(Permissions.IntegrationsEdit)] public Task<CarrierConnectionRow> Test(Guid id, CancellationToken ct) => svc.TestAsync(id, ct);
    /// <summary>Production approval by a second person (integrations.approve, not the tester) for the tested configuration.</summary>
    [HttpPost("carrier-connections/{id:guid}/production-approval")] [RequirePermission(Permissions.IntegrationsApprove)] public Task<CarrierConnectionRow> Approve(Guid id, CarrierApprovalInput r, CancellationToken ct) => svc.ApproveProductionAsync(id, r, ct);
    [HttpPut("carrier-connections/{id:guid}/enabled")] [RequirePermission(Permissions.IntegrationsEdit)] public Task<CarrierConnectionRow> Enable(Guid id, CarrierEnableInput r, CancellationToken ct) => svc.SetEnabledAsync(id, r.Enabled, ct);
    [HttpGet("carrier-connections/{id:guid}/requests")] public Task<List<RequestLogRow>> Requests(Guid id, [FromQuery] int take = 100, CancellationToken ct = default) => svc.ConnectionLogAsync(id, take, ct);

    [HttpGet("shipments/{id:guid}/carrier")] public Task<ShipmentCarrierView> View(Guid id, CancellationToken ct) => svc.ViewAsync(id, ct);
    [HttpPut("shipments/{id:guid}/carrier/ship-to")] public async Task<IActionResult> ShipTo(Guid id, ShipToInput r, CancellationToken ct) { await svc.SaveShipToAsync(id, r, ct); return NoContent(); }
    [HttpPost("shipments/{id:guid}/carrier/validate-address")] public Task<AddressResult> ValidateAddress(Guid id, CarrierChoice r, CancellationToken ct) => svc.ValidateAddressAsync(id, r, ct);
    [HttpPost("shipments/{id:guid}/carrier/rates")] public Task<List<RateQuote>> Rates(Guid id, CarrierChoice r, CancellationToken ct) => svc.RatesAsync(id, r, ct);
    /// <summary>Selects a service; the charge is taken from a fresh carrier quote.</summary>
    [HttpPost("shipments/{id:guid}/carrier/service")] public Task<RateQuote> Service(Guid id, ServiceChoice r, CancellationToken ct) => svc.SelectServiceAsync(id, r, ct);
    /// <summary>Creates the carrier shipment and labels (idempotent: existing active labels are returned).</summary>
    [HttpPost("shipments/{id:guid}/carrier/labels")] public Task<List<LabelRow>> Labels(Guid id, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateLabelsInput? r, CancellationToken ct) => svc.CreateLabelsAsync(id, r, ct);
    [HttpPost("shipments/{id:guid}/carrier/void")] public Task<VoidResult> Void(Guid id, VoidLabelsInput r, CancellationToken ct) => svc.VoidLabelsAsync(id, r, ct);
    [HttpPost("shipments/{id:guid}/carrier/track")] public Task<TrackResult> Track(Guid id, CancellationToken ct) => svc.TrackAsync(id, ct);
    [HttpGet("carrier-labels/{labelId:guid}")] public async Task<FileContentResult> Label(Guid labelId, CancellationToken ct) { var (d, type, name) = await svc.LabelFileAsync(labelId, ct); return File(d, type, name); }
}

/// <summary>Shipping document templates (bill of lading, packing list, manifest) with approval-controlled revisions, and issued documents.</summary>
[ApiController, Authorize, Route("api/shipping")]
public sealed class ShippingDocumentController(ShippingDocumentService svc) : ControllerBase
{
    /// <summary>Section types, placeholders and the built-in layouts.</summary>
    [HttpGet("document-sections")] [RequirePermission(Permissions.ShippingView)] public SectionCatalog Sections() => svc.Catalog();
    [HttpGet("document-templates")] public Task<List<TemplateRow>> Templates([FromQuery] ShippingDocumentKind? kind, CancellationToken ct) => svc.TemplatesAsync(kind, ct);
    [HttpGet("document-templates/{id:guid}")] public Task<TemplateDetail> Template(Guid id, CancellationToken ct) => svc.TemplateAsync(id, ct);
    [HttpPost("document-templates")] public async Task<TemplateDetail> Create(TemplateInput r, CancellationToken ct) => await svc.TemplateAsync(await svc.CreateAsync(r, ct), ct);
    [HttpPut("document-templates/{id:guid}")] public async Task<IActionResult> Update(Guid id, TemplateUpdate r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }
    /// <summary>Starts a draft revision copied from the latest approved one (one open revision at a time).</summary>
    [HttpPost("document-templates/{id:guid}/revisions")] public async Task<object> NewRevision(Guid id, CancellationToken ct) => new { id = await svc.NewRevisionAsync(id, ct) };
    [HttpPut("document-template-revisions/{id:guid}")] public async Task<IActionResult> SaveRevision(Guid id, RevisionInput r, CancellationToken ct) { await svc.SaveRevisionAsync(id, r, ct); return NoContent(); }
    /// <summary>Header logo (JPEG, up to 512 KB) on a draft revision.</summary>
    [HttpPost("document-template-revisions/{id:guid}/logo")] [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Logo(Guid id, IFormFile file, CancellationToken ct)
    {
        if (file.Length > ShippingDocumentService.MaxLogoBytes) throw new AppException("The logo must be at most 512 KB.");
        using var ms = new MemoryStream(); await file.CopyToAsync(ms, ct);
        await svc.SetLogoAsync(id, ms.ToArray(), ct); return NoContent();
    }
    [HttpDelete("document-template-revisions/{id:guid}/logo")] public async Task<IActionResult> RemoveLogo(Guid id, CancellationToken ct) { await svc.SetLogoAsync(id, null, ct); return NoContent(); }
    [HttpGet("document-template-revisions/{id:guid}/logo")] public async Task<FileContentResult> GetLogo(Guid id, CancellationToken ct) => File(await svc.LogoAsync(id, ct), "image/jpeg");
    [HttpPost("document-template-revisions/{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await svc.SubmitAsync(id, ct); return NoContent(); }
    /// <summary>Approve (shipping.approve, not the preparer or submitter); effective at the later of now and the requested date.</summary>
    [HttpPost("document-template-revisions/{id:guid}/approve")] public async Task<IActionResult> Approve(Guid id, ApproveRevisionInput r, CancellationToken ct) { await svc.ApproveAsync(id, r, ct); return NoContent(); }
    [HttpPost("document-template-revisions/{id:guid}/reject")] public async Task<IActionResult> Reject(Guid id, RejectRevisionInput r, CancellationToken ct) { await svc.RejectAsync(id, r, ct); return NoContent(); }
    /// <summary>Preview with sample data, or with a shipment the caller can see. Every page is marked as a preview.</summary>
    [HttpGet("document-template-revisions/{id:guid}/preview")] public async Task<FileContentResult> Preview(Guid id, [FromQuery] Guid? shipmentId, CancellationToken ct) => File(await svc.PreviewAsync(id, shipmentId, ct), "application/pdf", "preview.pdf");
    /// <summary>Manifest of everything shipped with one carrier from one site on one site-local day; recorded like other documents.</summary>
    [HttpGet("manifest")]
    public async Task<FileContentResult> Manifest([FromQuery] Guid siteId, [FromQuery] Guid carrierId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var (docId, pdf) = await svc.ManifestAsync(siteId, carrierId, date, ct);
        Response.Headers["X-Document-Id"] = docId.ToString();
        return File(pdf, "application/pdf", "manifest.pdf");
    }
    [HttpGet("generated-documents")] public Task<List<GeneratedRow>> Issued([FromQuery] Guid siteId, [FromQuery] ShippingDocumentKind? kind, CancellationToken ct) => svc.IssuedListAsync(null, siteId, kind, ct);
    /// <summary>The issued PDF exactly as it was issued.</summary>
    [HttpGet("generated-documents/{id:guid}")] public async Task<FileContentResult> IssuedPdf(Guid id, CancellationToken ct) { var (pdf, name) = await svc.IssuedPdfAsync(id, ct); return File(pdf, "application/pdf", name); }
    /// <summary>Re-renders the stored snapshot with the template revision used and compares it with the issued PDF.</summary>
    [HttpPost("generated-documents/{id:guid}/reproduce")] public Task<ReproduceResult> Reproduce(Guid id, CancellationToken ct) => svc.ReproduceAsync(id, ct);
}

