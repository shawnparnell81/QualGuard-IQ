using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Inventory;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Warehouse locations, stock, the inventory ledger, holds, issues, transfers, counts and ERP mirror mode.</summary>
[ApiController, Authorize, Route("api/warehouse")]
public sealed class WarehouseController(InventoryService inv, CountService counts, WarehouseInsights insights) : ControllerBase
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
    [HttpPost("scrap")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> Scrap(ScrapInput r, CancellationToken ct) => Tx(await inv.ScrapAsync(r, ct));
    [HttpPost("reservations")] [RequirePermission(Permissions.InventoryEdit)] public async Task<IActionResult> Reserve(ReserveInput r, CancellationToken ct) { await inv.ReserveAsync(r, ct); return NoContent(); }

    [HttpGet("holds")] [RequirePermission(Permissions.InventoryView)] public Task<List<InventoryHold>> Holds([FromQuery] HoldStatus? status, [FromQuery] Guid? lotId, CancellationToken ct) => inv.HoldsAsync(status, lotId, ct);
    [HttpPost("holds")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> CreateHold(HoldInput r, CancellationToken ct) { var h = await inv.CreateHoldAsync(r, ct); return new { h.Id, h.Number }; }
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
public sealed class ReceivingController(ReceivingService svc, InventoryService inv) : ControllerBase
{
    [HttpGet("receipts")] [RequirePermission(Permissions.InventoryView)] public Task<List<Receipt>> Receipts([FromQuery] ReceiptStatus? status, CancellationToken ct) => svc.ListAsync(status, ct);
    [HttpPost("receipts")] [RequirePermission(Permissions.InventoryEdit)] public async Task<object> CreateReceipt(ReceiptInput r, CancellationToken ct) { var x = await svc.CreateAsync(r, ct); return new { x.Id, x.Number, x.Status }; }
    [HttpGet("receipts/{id:guid}")] public Task<ReceiptDetail> Receipt(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost("receipts/{id:guid}/lines/{lineId:guid}/inspection")] public async Task<IActionResult> LinkInspection(Guid id, Guid lineId, LinkInspectionInput r, CancellationToken ct) { await svc.LinkInspectionAsync(id, lineId, r, ct); return NoContent(); }
    /// <summary>Inspection outcome: Accept, Reject (quarantine + hold + NCR) or Quarantine (hold).</summary>
    [HttpPost("receipts/{id:guid}/lines/{lineId:guid}/decision")] public async Task<IActionResult> Decide(Guid id, Guid lineId, LineDecisionInput r, CancellationToken ct) { await svc.DecideLineAsync(id, lineId, r, ct); return NoContent(); }
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
public sealed class ShippingController(ShippingService svc, InventoryService inv) : ControllerBase
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
    [HttpPost("shipments/{id:guid}/containers")] public async Task<object> Pack(Guid id, PackInput r, CancellationToken ct) { var c = await svc.PackAsync(id, r, ct); return new { c.Id, c.Number }; }
    [HttpPost("shipments/{id:guid}/verify")] public Task<VerifyResult> Verify(Guid id, VerifyInput r, CancellationToken ct) => svc.VerifyAsync(id, r, ct);
    [HttpPost("shipments/{id:guid}/ship")] public async Task<IActionResult> Ship(Guid id, ShipInput r, CancellationToken ct) { await svc.ShipAsync(id, r, ct); return NoContent(); }
    [HttpPost("shipments/{id:guid}/delivered")] public async Task<IActionResult> Delivered(Guid id, DeliveredInput r, CancellationToken ct) { await svc.DeliveredAsync(id, r, ct); return NoContent(); }
    [HttpPost("shipments/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) { await svc.CancelAsync(id, ct); return NoContent(); }
    [HttpGet("shipments/{id:guid}/packing-list")] public async Task<FileContentResult> PackingList(Guid id, CancellationToken ct) => File(await svc.PackingListAsync(id, ct), "application/pdf", "packing-list.pdf");
    [HttpGet("shipments/{id:guid}/bill-of-lading")] public async Task<FileContentResult> Bol(Guid id, CancellationToken ct) => File(await svc.BillOfLadingAsync(id, ct), "application/pdf", "bill-of-lading.pdf");
    [HttpGet("shipments/{id:guid}/custody")] public Task<List<CustodyRow>> Custody(Guid id, CancellationToken ct) => inv.CustodyAsync(ShippingService.ShipmentType, id, ct);
}
