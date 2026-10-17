using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Inventory;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Logistics;

public sealed record PrinterInput(string Code, string Name, PrinterDriver Driver, string? Address, PrinterLanguage Language, int Dpi, decimal WidthMm, decimal HeightMm, bool Enabled, Guid SiteId, string? Secret = null, bool ClearSecret = false);
public sealed record PrinterRow(Guid Id, string Code, string Name, PrinterDriver Driver, string? Address, PrinterLanguage Language, int Dpi, decimal WidthMm, decimal HeightMm, bool Enabled,
    PrinterStatus Status, DateTimeOffset? LastCheckedAt, string? LastError, bool HasSecret, Guid SiteId, string SiteName, int Pending, int Failed);
public sealed record RouteInput(LabelType LabelType, Guid? SiteId, Guid? LocationId, string? Workstation, Guid? UserId, Guid PrinterId, int Copies, bool AutoPrint, PrintMode Mode, bool Active);
public sealed record RouteRow(LabelPrintRoute Route, string Printer, string? Site, string? Location, string? User);
public sealed record PrintRequest(LabelType LabelType, string RefType, Guid RefId, Guid? PrinterId, int? Copies);
public sealed record ReprintInput(string Reason, Guid? PrinterId, int? Copies);
public sealed record PrintOutcome(Guid JobId, LabelType LabelType, string Title, PrintJobStatus Status, string? Printer, string? Error);
public sealed record JobRow(LabelPrintJob Job, string? Printer, string RequestedBy);
public sealed record JobPage(List<JobRow> Items, int Total);
public sealed record RouteResolution(LabelPrintRoute? Route, LabelPrinter? Printer, string Explanation);

/// <summary>
/// Label print routing: printers per site, routes per label type (site, warehouse or zone, workstation, user), print jobs with immediate
/// or queued printing, retry with back-off, reprint with a reason, preview and a PDF fallback. A print failure never fails the
/// transaction that asked for the label: the transaction is already saved, and the job shows the failure and can be retried or
/// downloaded. No physical printer is assumed; the virtual printer records output for testing.
/// </summary>
public sealed class LabelPrintService(AppDbContext db, ModuleSupport m, IEnumerable<IPrintTransport> transports, SecretProtector secrets, ILogger<LabelPrintService> log, IHttpContextAccessor? http = null)
{
    public const string JobType = "LabelPrintJob";
    public const int MaxAttempts = 5;
    static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    string? Workstation => http?.HttpContext?.Request.Headers["X-Workstation"].FirstOrDefault() is { Length: > 0 and <= 100 } w ? w.Trim() : null;

    // ------------------------------------------------------------------ printers
    public async Task<List<PrinterRow>> PrintersAsync(Guid? siteId, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.LabelPrinters.AsNoTracking(), Permissions.InventoryView, ct);
        if (siteId is { } s) q = q.Where(x => x.SiteId == s);
        return await (from p in q
                      join site in db.Sites.AsNoTracking() on p.SiteId equals site.Id
                      orderby site.Name, p.Code
                      select new PrinterRow(p.Id, p.Code, p.Name, p.Driver, p.Address, p.Language, p.Dpi, p.WidthMm, p.HeightMm, p.Enabled, p.Status, p.LastCheckedAt, p.LastError, p.SecretCiphertext != null, p.SiteId, site.Name,
                          db.LabelPrintJobs.Count(j => j.PrinterId == p.Id && (j.Status == PrintJobStatus.Pending || j.Status == PrintJobStatus.Held)),
                          db.LabelPrintJobs.Count(j => j.PrinterId == p.Id && j.Status == PrintJobStatus.Failed))).ToListAsync(ct);
    }

    public async Task<LabelPrinter> SavePrinterAsync(Guid? id, PrinterInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Code, "Printer code", 40); ModuleSupport.Required(r.Name, "Printer name", 200);
        if (r.Dpi is not (152 or 203 or 300 or 600)) throw new AppException("Resolution must be 152, 203, 300 or 600 dpi.");
        if (r.WidthMm is < 20 or > 220 || r.HeightMm is < 10 or > 400) throw new AppException("Label size must be 20–220 mm wide and 10–400 mm high.");
        if (r.Address?.Length > 500) throw new AppException("The address is too long.");
        try
        {
            if (r.Driver == PrinterDriver.RawTcp) RawTcpPrintTransport.Parse(r.Address);
            if (r.Driver == PrinterDriver.HttpRelay) HttpRelayPrintTransport.Validate(r.Address);
        }
        catch (PrintFailure e) { throw new AppException(e.Message); }
        if (r.Driver == PrinterDriver.RawTcp && r.Language != PrinterLanguage.Zpl) throw new AppException("Raw TCP printers take ZPL.");
        if (r.Secret?.Length > 2000) throw new AppException("The token is too long.");
        LabelPrinter p;
        if (id is { } pid)
        {
            p = await db.LabelPrinters.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw new NotFoundException("Printer");
            await m.RequireAsync(Permissions.InventoryApprove, p.SiteId, null, ct);
            if (r.SiteId != p.SiteId) throw new AppException("A printer cannot move to another site; add it at the other site.");
        }
        else
        {
            await m.RequireAsync(Permissions.InventoryApprove, r.SiteId, null, ct);
            await m.ValidateLocationAsync(r.SiteId, null, ct);
            p = new LabelPrinter { SiteId = r.SiteId };
            db.LabelPrinters.Add(p);
        }
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.LabelPrinters.AnyAsync(x => x.SiteId == r.SiteId && x.Code == code && x.Id != p.Id, ct)) throw new ConflictException($"Printer code {code} is already used at this site.");
        var changedTarget = p.Driver != r.Driver || p.Address != r.Address?.Trim();
        p.Code = code; p.Name = r.Name.Trim(); p.Driver = r.Driver; p.Address = string.IsNullOrWhiteSpace(r.Address) ? null : r.Address.Trim(); p.Language = r.Language;
        p.Dpi = r.Dpi; p.WidthMm = r.WidthMm; p.HeightMm = r.HeightMm; p.Enabled = r.Enabled;
        if (!string.IsNullOrEmpty(r.Secret)) p.SecretCiphertext = secrets.Protect(r.Secret);
        else if (r.ClearSecret) p.SecretCiphertext = null;
        if (changedTarget) { p.Status = PrinterStatus.Unknown; p.LastError = null; p.LastCheckedAt = null; }
        await db.SaveChangesAsync(ct);
        return p;
    }

    /// <summary>Prints a test label; the printer status records the result. Returns the job.</summary>
    public async Task<PrintOutcome> TestPrinterAsync(Guid id, CancellationToken ct)
    {
        var p = await db.LabelPrinters.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Printer");
        await m.RequireAsync(Permissions.InventoryApprove, p.SiteId, null, ct);
        var site = await db.Sites.AsNoTracking().Where(x => x.Id == p.SiteId).Select(x => x.Name).FirstAsync(ct);
        var data = new LabelData(LabelType.Bin, "LabelPrinter", p.Id, p.Code, $"Test label - {p.Code}", "QG:TEST:" + p.Code, LabelSymbology.Code128,
            [new("Printer", p.Name), new("Site", site), new("Size", $"{p.WidthMm:0.#} x {p.HeightMm:0.#} mm, {p.Dpi} dpi"), new("Sent", m.Now.ToString("yyyy-MM-dd HH:mm 'UTC'"))], null, 1, p.SiteId, null, null);
        var job = NewJob(data, PrintTrigger.Manual, p, null, 1);
        await db.SaveChangesAsync(ct);
        return await AttemptAsync(job.Id, ct);
    }

    // ------------------------------------------------------------------ routes
    public async Task<List<RouteRow>> RoutesAsync(CancellationToken ct)
    {
        var scope = await m.ScopeAsync(db.LabelPrinters.AsNoTracking(), Permissions.InventoryView, ct);
        return await (from r in db.LabelPrintRoutes.AsNoTracking()
                      join p in scope on r.PrinterId equals p.Id
                      orderby r.LabelType, r.SiteId, r.CreatedAt
                      select new RouteRow(r, p.Code + " - " + p.Name, db.Sites.Where(s => s.Id == r.SiteId).Select(s => s.Name).FirstOrDefault(),
                          db.WarehouseLocations.Where(l => l.Id == r.LocationId).Select(l => l.Code).FirstOrDefault(), db.Users.Where(u => u.Id == r.UserId).Select(u => u.DisplayName).FirstOrDefault())).ToListAsync(ct);
    }

    public async Task<LabelPrintRoute> SaveRouteAsync(Guid? id, RouteInput r, CancellationToken ct)
    {
        var printer = await db.LabelPrinters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PrinterId, ct) ?? throw new AppException("Select a printer.");
        if (r.Copies is < 1 or > 50) throw new AppException("Copies must be 1–50.");
        if (r.Workstation?.Trim().Length > 100) throw new AppException("The workstation name is too long.");
        var site = r.SiteId;
        if (r.LocationId is { } lid)
        {
            var loc = await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == lid, ct) ?? throw new AppException("The warehouse or zone was not found.");
            if (site is not null && site != loc.SiteId) throw new AppException("The warehouse or zone belongs to another site.");
            site = loc.SiteId;
        }
        if (site is { } s)
        {
            if (printer.SiteId != s) throw new AppException("The printer is at another site.");
            await m.RequireAsync(Permissions.InventoryApprove, s, null, ct);
        }
        else if (!await m.HasAllSitesAsync(Permissions.InventoryApprove, ct)) throw new ForbiddenException("Organization-wide routes need inventory approval rights at every site.");
        await m.RequireAsync(Permissions.InventoryApprove, printer.SiteId, null, ct);
        if (r.UserId is { } u) await m.RequireActiveUserAsync(u, "user", ct);
        LabelPrintRoute route;
        if (id is { } rid)
        {
            route = await db.LabelPrintRoutes.FirstOrDefaultAsync(x => x.Id == rid, ct) ?? throw new NotFoundException("Route");
            if (route.SiteId is { } old) await m.RequireAsync(Permissions.InventoryApprove, old, null, ct);
            else if (!await m.HasAllSitesAsync(Permissions.InventoryApprove, ct)) throw new ForbiddenException();
        }
        else { route = new LabelPrintRoute(); db.LabelPrintRoutes.Add(route); }
        route.LabelType = r.LabelType; route.SiteId = site; route.LocationId = r.LocationId; route.Workstation = string.IsNullOrWhiteSpace(r.Workstation) ? null : r.Workstation.Trim();
        route.UserId = r.UserId; route.PrinterId = r.PrinterId; route.Copies = r.Copies; route.AutoPrint = r.AutoPrint; route.Mode = r.Mode; route.Active = r.Active;
        await db.SaveChangesAsync(ct);
        return route;
    }

    /// <summary>The most specific active route for a label: user, workstation, deepest warehouse/zone, site, organization.</summary>
    public async Task<RouteResolution> ResolveAsync(LabelType type, Guid siteId, Guid? locationId, string? workstation, Guid? userId, CancellationToken ct)
    {
        var routes = await db.LabelPrintRoutes.AsNoTracking().Where(x => x.Active && x.LabelType == type && (x.SiteId == null || x.SiteId == siteId)).ToListAsync(ct);
        var ancestors = new Dictionary<Guid, int>();
        if (locationId is { } lid)
        {
            var depth = 0; Guid? cur = lid;
            while (cur is { } c && depth < 30 && !ancestors.ContainsKey(c))
            {
                ancestors[c] = depth++;
                cur = await db.WarehouseLocations.AsNoTracking().Where(x => x.Id == c).Select(x => x.ParentId).FirstOrDefaultAsync(ct);
            }
        }
        var best = routes
            .Where(r => (r.UserId is null || r.UserId == userId) && (r.Workstation is null || string.Equals(r.Workstation, workstation, StringComparison.OrdinalIgnoreCase)) && (r.LocationId is null || ancestors.ContainsKey(r.LocationId.Value)))
            .Select(r => (Route: r, Score: (r.UserId is null ? 0 : 10000) + (r.Workstation is null ? 0 : 1000) + (r.LocationId is { } l ? 100 + (30 - ancestors[l]) : 0) + (r.SiteId is null ? 0 : 1)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Route.CreatedAt).ThenBy(x => x.Route.Id).FirstOrDefault();
        if (best.Route is null) return new(null, null, $"No active route for {type} labels; print manually or download the PDF.");
        var printer = await db.LabelPrinters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == best.Route.PrinterId, ct);
        var why = string.Join(", ", new[] { best.Route.UserId is not null ? "user" : null, best.Route.Workstation is not null ? $"workstation {best.Route.Workstation}" : null, best.Route.LocationId is not null ? "warehouse/zone" : null, best.Route.SiteId is not null ? "site" : "organization" }.Where(x => x is not null));
        return new(best.Route, printer, $"Route for {type} labels matched on {why}: {printer?.Code}.");
    }

    // ------------------------------------------------------------------ label sources
    /// <summary>Builds the label(s) for a reference. Receiving gives the receipt label; Lot/Serial take a lot or serial id; Inventory a stock balance id.</summary>
    public async Task<List<LabelData>> BuildAsync(LabelType type, string refType, Guid refId, CancellationToken ct)
    {
        string? Fmt(decimal? q) => q?.ToString("0.####");
        async Task<string> Part(Guid id) => await db.Parts.AsNoTracking().Where(x => x.Id == id).Select(x => x.PartNumber + (x.Name != null ? " - " + x.Name : "")).FirstOrDefaultAsync(ct) ?? "";
        async Task<string?> PartNo(Guid? id) => id is { } p ? await db.Parts.AsNoTracking().Where(x => x.Id == p).Select(x => x.PartNumber).FirstOrDefaultAsync(ct) : null;
        async Task<string?> Loc(Guid? id) => id is { } l ? await db.WarehouseLocations.AsNoTracking().Where(x => x.Id == l).Select(x => x.Code).FirstOrDefaultAsync(ct) : null;
        async Task<string?> LotNo(Guid? id) => id is { } l ? await db.Lots.AsNoTracking().Where(x => x.Id == l).Select(x => x.Number).FirstOrDefaultAsync(ct) : null;
        async Task<string?> Sn(Guid? id) => id is { } s ? await db.SerialUnits.AsNoTracking().Where(x => x.Id == s).Select(x => x.SerialNumber).FirstOrDefaultAsync(ct) : null;
        switch (type)
        {
            case LabelType.Receiving:
            {
                var r = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) ?? throw new NotFoundException("Receipt");
                var sup = r.SupplierId is { } s ? await db.Suppliers.AsNoTracking().Where(x => x.Id == s).Select(x => x.OperatingName ?? x.LegalName).FirstOrDefaultAsync(ct) : null;
                var lines = await db.ReceiptLines.AsNoTracking().CountAsync(x => x.ReceiptId == r.Id, ct);
                return [new(type, ReceivingService.ReceiptType, r.Id, r.Number, $"Receipt {r.Number}", $"QG:REC:Receipt:{r.Id}", LabelSymbology.Qr,
                    [new("Supplier", sup), new("PO", r.PurchaseOrderNumber), new("Packing slip", r.PackingSlip), new("Received", r.ReceivedOn.ToString("yyyy-MM-dd")), new("Lines", lines.ToString()), new("Dock", await Loc(r.ReceivingLocationId))],
                    null, 1, r.SiteId, r.DepartmentId, r.ReceivingLocationId)];
            }
            case LabelType.Lot:
            {
                var l = await db.Lots.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) ?? throw new NotFoundException("Lot");
                return [new(type, "Lot", l.Id, l.Number, $"Lot {l.Number}", "QG:LOT:" + l.Number, LabelSymbology.Code128,
                    [new("Part", await Part(l.PartId)), new("Revision", l.Revision), new("Quantity", $"{Fmt(l.Quantity)} {l.UnitOfMeasure}"), new("Supplier lot", l.SupplierLotNumber), new("Expires", l.ExpiresOn?.ToString("yyyy-MM-dd"))],
                    null, 1, l.SiteId, l.DepartmentId, null)];
            }
            case LabelType.Serial:
            {
                var s = await db.SerialUnits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) ?? throw new NotFoundException("Serial");
                var pn = await PartNo(s.PartId);
                return [new(type, "SerialUnit", s.Id, s.SerialNumber, $"SN {s.SerialNumber}", $"QG:SN:{pn}:{s.SerialNumber}", LabelSymbology.Code128,
                    [new("Part", await Part(s.PartId)), new("Revision", s.Revision), new("Lot", await LotNo(s.LotId))], null, 1, s.SiteId, s.DepartmentId, null)];
            }
            case LabelType.Bin:
            {
                var l = await db.WarehouseLocations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) ?? throw new NotFoundException("Location");
                return [new(type, InventoryService.LocationType, l.Id, l.Code, l.Code, "QG:LOC:" + l.Code, LabelSymbology.Code128, [new("Name", l.Name), new("Path", l.Path), new("Kind", l.Kind.ToString()), new("Purpose", l.Purpose.ToString())],
                    null, 1, l.SiteId, l.DepartmentId, l.Id)];
            }
            case LabelType.Inventory:
            {
                var b = await db.InventoryBalances.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) ?? throw new NotFoundException("Stock");
                var lot = await LotNo(b.LotId); var sn = await Sn(b.SerialId); var pn = await PartNo(b.PartId);
                return [new(type, "InventoryBalance", b.Id, sn ?? lot ?? pn, $"{pn}{(b.Revision is { } rv ? " rev " + rv : "")}", sn is not null ? $"QG:SN:{pn}:{sn}" : lot is not null ? "QG:LOT:" + lot : "QG:PART:" + pn, LabelSymbology.Code128,
                    [new("Part", await Part(b.PartId)), new("Lot", lot), new("Serial", sn), new("Quantity", Fmt(b.OnHand)), new("Location", await Loc(b.LocationId)), new("Status", b.Status.ToString())], null, 1, b.SiteId, b.DepartmentId, b.LocationId)];
            }
            case LabelType.Carton or LabelType.Pallet:
            {
                var c = await db.ShipmentContainers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) ?? throw new NotFoundException("Container");
                var s = await db.Shipments.AsNoTracking().FirstAsync(x => x.Id == c.ShipmentId, ct);
                var cust = s.CustomerId is { } cid ? await db.Customers.AsNoTracking().Where(x => x.Id == cid).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
                var all = await db.ShipmentContainers.AsNoTracking().Where(x => x.ShipmentId == s.Id && x.ParentId == c.ParentId && x.Kind == c.Kind).OrderBy(x => x.Number).Select(x => x.Id).ToListAsync(ct);
                var items = await db.ContainerItems.AsNoTracking().Where(x => x.ContainerId == c.Id).ToListAsync(ct);
                var children = await db.ShipmentContainers.AsNoTracking().CountAsync(x => x.ParentId == c.Id, ct);
                return [new(c.Kind == ContainerKind.Pallet ? LabelType.Pallet : LabelType.Carton, ShippingService.ContainerType, c.Id, c.Number, $"{c.Kind} {c.Number}", "QG:CTN:" + c.Number, LabelSymbology.Code128,
                    [new("Shipment", s.Number), new("Ship to", cust ?? s.ShipTo), new("Customer PO", s.CustomerPo), new($"{c.Kind}", $"{all.IndexOf(c.Id) + 1} of {all.Count}"),
                     new("Contents", children > 0 ? $"{children} unit(s)" : $"{items.Count} line(s), {Fmt(items.Sum(i => i.Quantity))} pcs"), new("Weight", c.Weight is { } w ? $"{w:0.###} kg" : null)],
                    null, 1, s.SiteId, s.DepartmentId, s.StagingLocationId)];
            }
            case LabelType.Shipping:
            {
                var s = await db.Shipments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) ?? throw new NotFoundException("Shipment");
                var cust = s.CustomerId is { } cid ? await db.Customers.AsNoTracking().Where(x => x.Id == cid).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
                var carrier = s.CarrierId is { } car ? await db.Carriers.AsNoTracking().Where(x => x.Id == car).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
                var site = await db.Sites.AsNoTracking().Where(x => x.Id == s.SiteId).Select(x => x.Name).FirstAsync(ct);
                var units = await db.ShipmentContainers.AsNoTracking().CountAsync(x => x.ShipmentId == s.Id && x.ParentId == null, ct);
                return [new(type, ShippingService.ShipmentType, s.Id, s.Number, $"Ship to: {cust ?? s.ShipTo}", s.TrackingNumber is { Length: > 0 } t ? t : s.Number, LabelSymbology.Code128,
                    [new("From", site), new("Address", s.ShipTo), new("Shipment", s.Number), new("Customer PO", s.CustomerPo), new("Carrier", carrier), new("Service", s.ServiceLevel), new("Tracking", s.TrackingNumber), new("BOL", s.BolNumber), new("Handling units", units.ToString())],
                    null, 1, s.SiteId, s.DepartmentId, s.StagingLocationId)];
            }
            case LabelType.Hold or LabelType.Quarantine:
            {
                var h = await db.InventoryHolds.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId, ct) ?? throw new NotFoundException("Hold");
                return [new(type, InventoryService.HoldType, h.Id, h.Number, $"{(type == LabelType.Quarantine ? "Quarantine" : "Hold")} {h.Number}", $"QG:REC:{InventoryService.HoldType}:{h.Id}", LabelSymbology.Qr,
                    [new("Reason", h.Reason), new("Kind", h.Kind.ToString()), new("Part", h.PartId is { } p ? await Part(p) : null), new("Lot", await LotNo(h.LotId)), new("Serial", await Sn(h.SerialId)), new("Location", await Loc(h.LocationId)), new("Placed", h.CreatedAt.ToString("yyyy-MM-dd"))],
                    type == LabelType.Quarantine ? "QUARANTINE - DO NOT USE" : "ON HOLD - DO NOT USE", 1, h.SiteId, h.DepartmentId, h.LocationId)];
            }
            case LabelType.Scrap:
            {
                var t = await db.InventoryTransactions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == refId && x.Kind == InventoryTxKind.Scrap, ct) ?? throw new NotFoundException("Scrap transaction");
                return [new(type, InventoryService.TxType, t.Id, t.Number, $"Scrap {t.Number}", $"QG:REC:{InventoryService.TxType}:{t.Id}", LabelSymbology.Qr,
                    [new("Part", await Part(t.PartId)), new("Lot", await LotNo(t.LotId)), new("Serial", await Sn(t.SerialId)), new("Quantity", $"{Fmt(t.Quantity)} {t.UnitOfMeasure}"), new("Reason", t.Reason), new("From", await Loc(t.FromLocationId)), new("Date", t.OccurredAt.ToString("yyyy-MM-dd"))],
                    "SCRAP - DO NOT USE", 1, t.SiteId, t.DepartmentId, t.FromLocationId)];
            }
        }
        throw new AppException("Unknown label type.");
    }

    /// <summary>Labels a receipt produces: the receipt label, one lot label per line and one per serial, with the receiving requirement's copies.</summary>
    async Task<List<LabelData>> ReceiptLabelsAsync(Guid receiptId, CancellationToken ct)
    {
        var res = await BuildAsync(LabelType.Receiving, ReceivingService.ReceiptType, receiptId, ct);
        var rc = await db.Receipts.AsNoTracking().FirstAsync(x => x.Id == receiptId, ct);
        foreach (var l in await db.ReceiptLines.AsNoTracking().Where(x => x.ReceiptId == receiptId).OrderBy(x => x.LineNumber).ToListAsync(ct))
        {
            var copies = l.ReceivingRequirementId is { } rq ? await db.ReceivingRequirements.AsNoTracking().Where(x => x.Id == rq).Select(x => x.LabelCopies).FirstAsync(ct) : 1;
            var put = l.PutAwayLocationId ?? rc.ReceivingLocationId;
            if (l.LotId is { } lid) res.AddRange((await BuildAsync(LabelType.Lot, "Lot", lid, ct)).Select(d => d with { Copies = Math.Max(1, copies), LocationId = put }));
            var pn = await db.Parts.AsNoTracking().Where(x => x.Id == l.PartId).Select(x => x.PartNumber).FirstAsync(ct);
            foreach (var sn in (l.SerialNumbers ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (await db.SerialUnits.AsNoTracking().FirstOrDefaultAsync(x => x.PartId == l.PartId && x.SerialNumber == sn, ct) is { } unit)
                    res.AddRange((await BuildAsync(LabelType.Serial, "SerialUnit", unit.Id, ct)).Select(d => d with { Copies = Math.Max(1, copies), LocationId = put }));
        }
        return res;
    }

    async Task RequireViewAsync(LabelData d, CancellationToken ct)
    {
        var perm = d.Type is LabelType.Carton or LabelType.Pallet or LabelType.Shipping ? Permissions.ShippingView : Permissions.InventoryView;
        if (!await m.CanAsync(perm, d.SiteId, d.DepartmentId, ct)) throw new ForbiddenException("You cannot print labels for this record.");
    }

    async Task RequireOperateAsync(LabelPrintJob j, CancellationToken ct)
    {
        if (!await m.CanAsync(Permissions.InventoryEdit, j.SiteId, j.DepartmentId, ct) && !await m.CanAsync(Permissions.ShippingEdit, j.SiteId, j.DepartmentId, ct))
            throw new ForbiddenException("You cannot change label prints at this site.");
    }

    // ------------------------------------------------------------------ printing
    LabelPrintJob NewJob(LabelData d, PrintTrigger trigger, LabelPrinter? printer, LabelPrintRoute? route, int copies, Guid? reprintOf = null, string? reason = null)
    {
        var j = new LabelPrintJob
        {
            LabelType = d.Type, Trigger = trigger, RefType = d.RefType, RefId = d.RefId, RefNumber = d.RefNumber, Title = d.Title.Length > 300 ? d.Title[..300] : d.Title, Barcode = d.Barcode,
            DataJson = JsonSerializer.Serialize(d, Json), PrinterId = printer?.Id, RouteId = route?.Id, Copies = Math.Clamp(copies, 1, 999),
            Status = printer is null || printer.Driver == PrinterDriver.ManualOnly ? PrintJobStatus.ManualDownload : route?.Mode == PrintMode.Queue && trigger == PrintTrigger.Auto ? PrintJobStatus.Held : PrintJobStatus.Pending,
            RequestedById = m.UserId, RequestedAt = m.Now, Workstation = Workstation, ReprintOfId = reprintOf, ReprintReason = reason, LocationId = d.LocationId, SiteId = d.SiteId, DepartmentId = d.DepartmentId,
        };
        db.LabelPrintJobs.Add(j);
        return j;
    }

    /// <summary>
    /// Automatic labels after a completed transaction. Never throws: the transaction is already saved, so any failure is reported in the
    /// outcomes (and logged) instead of failing the request. Labels with no auto-print route are not printed.
    /// </summary>
    public async Task<List<PrintOutcome>> AutoAsync(LabelType type, string refType, Guid refId, CancellationToken ct)
    {
        try
        {
            var key = $"{refType}:{refId}:{type}";
            if (await db.LabelPrintJobs.AnyAsync(x => x.TriggerKey == key, ct)) return []; // a replayed request (offline queue) never prints twice
            var labels = type == LabelType.Receiving ? await ReceiptLabelsAsync(refId, ct) : await BuildAsync(type, refType, refId, ct);
            var jobs = new List<Guid>();
            foreach (var d in labels)
            {
                var (route, printer, _) = await ResolveAsync(d.Type, d.SiteId, d.LocationId, Workstation, m.UserId, ct);
                if (route is null || !route.AutoPrint) continue;
                var job = NewJob(d, PrintTrigger.Auto, printer, route, route.Copies * d.Copies);
                job.TriggerKey = key;
                jobs.Add(job.Id);
            }
            if (jobs.Count == 0) return [];
            await db.SaveChangesAsync(ct);
            var res = new List<PrintOutcome>();
            foreach (var id in jobs) res.Add(await AttemptAsync(id, ct));
            return res;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Automatic {Type} labels for {RefType} {RefId} could not be created", type, refType, refId);
            db.ChangeTracker.Clear();
            return [new PrintOutcome(Guid.Empty, type, $"{type} label", PrintJobStatus.Failed, null, "The label could not be prepared: " + (e is AppException ? e.Message : "unexpected error") + ". The transaction was saved; print the label manually.")];
        }
    }

    /// <summary>Manual print: the given printer, or the routed printer, or a PDF to download when no printer applies.</summary>
    public async Task<List<PrintOutcome>> PrintAsync(PrintRequest r, CancellationToken ct)
    {
        if (r.Copies is < 1 or > 50) throw new AppException("Copies must be 1–50.");
        var labels = await BuildAsync(r.LabelType, r.RefType, r.RefId, ct);
        var ids = new List<Guid>();
        foreach (var d in labels)
        {
            await RequireViewAsync(d, ct);
            LabelPrinter? printer = null; LabelPrintRoute? route = null;
            if (r.PrinterId is { } pid)
            {
                printer = await db.LabelPrinters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw new AppException("Select a printer.");
                if (printer.SiteId != d.SiteId) throw new AppException("The printer is at another site.");
            }
            else (route, printer, _) = await ResolveAsync(d.Type, d.SiteId, d.LocationId, Workstation, m.UserId, ct);
            ids.Add(NewJob(d, PrintTrigger.Manual, printer, route, r.Copies ?? route?.Copies ?? d.Copies).Id);
        }
        await db.SaveChangesAsync(ct);
        var res = new List<PrintOutcome>();
        foreach (var id in ids) res.Add(await AttemptAsync(id, ct));
        return res;
    }

    public async Task<PrintOutcome> ReprintAsync(Guid id, ReprintInput r, CancellationToken ct)
    {
        var old = await db.LabelPrintJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Print job");
        await RequireOperateAsync(old, ct);
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length < 3) throw new AppException("Give the reason for the reprint.");
        if (r.Reason.Length > 500) throw new AppException("The reason is too long.");
        if (r.Copies is < 1 or > 50) throw new AppException("Copies must be 1–50.");
        var data = JsonSerializer.Deserialize<LabelData>(old.DataJson, Json)!;
        var printer = r.PrinterId is { } pid ? await db.LabelPrinters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid && x.SiteId == old.SiteId, ct) ?? throw new AppException("Select a printer at the label's site.")
            : old.PrinterId is { } op ? await db.LabelPrinters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == op, ct) : null;
        var job = NewJob(data, PrintTrigger.Reprint, printer, null, r.Copies ?? old.Copies, old.Id, r.Reason.Trim());
        await db.SaveChangesAsync(ct);
        return await AttemptAsync(job.Id, ct);
    }

    public async Task<PrintOutcome> RetryAsync(Guid id, CancellationToken ct)
    {
        var j = await db.LabelPrintJobs.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Print job");
        await RequireOperateAsync(j, ct);
        if (j.Status is not (PrintJobStatus.Failed or PrintJobStatus.Held or PrintJobStatus.Pending)) throw new ConflictException($"The print job is {j.Status}; only failed, held or pending jobs can be sent.");
        if (j.PrinterId is null) throw new ConflictException("No printer is assigned; reprint it on a printer or download the PDF.");
        j.Status = PrintJobStatus.Pending; j.NextAttemptAt = null;
        await db.SaveChangesAsync(ct);
        return await AttemptAsync(id, ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var j = await db.LabelPrintJobs.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Print job");
        await RequireOperateAsync(j, ct);
        if (j.Status is PrintJobStatus.Printed or PrintJobStatus.Cancelled or PrintJobStatus.Printing) throw new ConflictException($"The print job is {j.Status}.");
        j.Status = PrintJobStatus.Cancelled; j.NextAttemptAt = null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sends one job. Claims it first (only one sender), records the outcome on the job and the printer, and never throws for print failures.</summary>
    public async Task<PrintOutcome> AttemptAsync(Guid jobId, CancellationToken ct)
    {
        var claimed = await db.LabelPrintJobs.Where(x => x.Id == jobId && (x.Status == PrintJobStatus.Pending || x.Status == PrintJobStatus.Failed))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, PrintJobStatus.Printing), ct);
        var job = await db.LabelPrintJobs.FirstAsync(x => x.Id == jobId, ct);
        if (claimed == 0) { await db.Entry(job).ReloadAsync(ct); return await OutcomeAsync(job, ct); }
        await db.Entry(job).ReloadAsync(ct);
        var printer = job.PrinterId is { } pid ? await db.LabelPrinters.FirstOrDefaultAsync(x => x.Id == pid, ct) : null;
        if (printer is null || printer.Driver == PrinterDriver.ManualOnly) { job.Status = PrintJobStatus.ManualDownload; await db.SaveChangesAsync(ct); return await OutcomeAsync(job, ct); }
        job.Attempts++;
        if (!printer.Enabled)
        {
            job.Status = PrintJobStatus.Failed; job.LastError = $"Printer {printer.Code} is disabled. Enable it or reprint on another printer."; job.NextAttemptAt = null;
            await db.SaveChangesAsync(ct);
            return await OutcomeAsync(job, ct);
        }
        try
        {
            var data = JsonSerializer.Deserialize<LabelData>(job.DataJson, Json)!;
            var (payload, type) = printer.Language == PrinterLanguage.Zpl
                ? (System.Text.Encoding.UTF8.GetBytes(LabelRenderer.Zpl(data, printer.Dpi, printer.WidthMm, printer.HeightMm, job.Copies)), "application/vnd.zebra-zpl")
                : (LabelRenderer.Pdf([(data, job.Copies)], printer.WidthMm, printer.HeightMm), "application/pdf");
            var transport = transports.FirstOrDefault(t => t.Driver == printer.Driver) ?? throw new PrintFailure($"No transport for {printer.Driver} printers.", offline: false);
            await transport.SendAsync(printer, secrets.Unprotect(printer.SecretCiphertext), payload, type, ct);
            job.Status = PrintJobStatus.Printed; job.PrintedAt = m.Now; job.LastError = null; job.NextAttemptAt = null;
            printer.Status = PrinterStatus.Online; printer.LastError = null; printer.LastCheckedAt = m.Now;
        }
        catch (Exception e) when (e is PrintFailure or System.Security.Cryptography.CryptographicException)
        {
            var offline = e is PrintFailure { Offline: true };
            job.Status = PrintJobStatus.Failed;
            job.LastError = (e is PrintFailure ? e.Message : "The printer token cannot be decrypted; enter it again.") is var msg && msg.Length > 1000 ? msg[..1000] : msg;
            job.NextAttemptAt = job.Attempts < MaxAttempts ? m.Now + Backoff[Math.Min(job.Attempts - 1, Backoff.Length - 1)] : null;
            printer.Status = offline ? PrinterStatus.Offline : PrinterStatus.Error; printer.LastError = job.LastError; printer.LastCheckedAt = m.Now;
        }
        await db.SaveChangesAsync(ct);
        return await OutcomeAsync(job, ct);
    }

    async Task<PrintOutcome> OutcomeAsync(LabelPrintJob j, CancellationToken ct) =>
        new(j.Id, j.LabelType, j.Title, j.Status, j.PrinterId is { } p ? await db.LabelPrinters.AsNoTracking().Where(x => x.Id == p).Select(x => x.Code).FirstOrDefaultAsync(ct) : null, j.LastError);

    /// <summary>Background: sends pending jobs and retries failed ones whose back-off has passed (enabled printers only).</summary>
    public async Task<int> ProcessDueAsync(CancellationToken ct)
    {
        var now = m.Now;
        var due = await db.LabelPrintJobs.AsNoTracking()
            .Where(x => x.PrinterId != null && (x.Status == PrintJobStatus.Pending || (x.Status == PrintJobStatus.Failed && x.NextAttemptAt != null && x.NextAttemptAt <= now)))
            .Where(x => db.LabelPrinters.Any(p => p.Id == x.PrinterId && p.Enabled))
            .OrderBy(x => x.RequestedAt).Select(x => x.Id).Take(200).ToListAsync(ct);
        var n = 0;
        foreach (var id in due) { if ((await AttemptAsync(id, ct)).Status == PrintJobStatus.Printed) n++; }
        return n;
    }

    // ------------------------------------------------------------------ queue and documents
    public async Task<JobPage> JobsAsync(Guid? siteId, PrintJobStatus? status, Guid? printerId, string? refType, Guid? refId, int page, int pageSize, CancellationToken ct)
    {
        var inv = await m.TryScopeAsync(db.LabelPrintJobs.AsNoTracking(), Permissions.InventoryView, ct);
        var shp = await m.TryScopeAsync(db.LabelPrintJobs.AsNoTracking(), Permissions.ShippingView, ct);
        if (inv is null && shp is null) throw new ForbiddenException();
        var q = inv is not null && shp is not null ? inv.Union(shp) : (inv ?? shp)!;
        if (siteId is { } s) q = q.Where(x => x.SiteId == s);
        if (status is { } st) q = q.Where(x => x.Status == st);
        if (printerId is { } p) q = q.Where(x => x.PrinterId == p);
        if (!string.IsNullOrEmpty(refType)) q = q.Where(x => x.RefType == refType);
        if (refId is { } r) q = q.Where(x => x.RefId == r);
        pageSize = Math.Clamp(pageSize, 1, 200); page = Math.Max(page, 1);
        var total = await q.CountAsync(ct);
        var items = await (from j in q
                           join u in db.Users.AsNoTracking() on j.RequestedById equals u.Id
                           orderby j.RequestedAt descending, j.Id descending
                           select new JobRow(j, db.LabelPrinters.Where(x => x.Id == j.PrinterId).Select(x => x.Code).FirstOrDefault(), u.DisplayName)).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new JobPage(items, total);
    }

    async Task<LabelPrintJob> ViewJobAsync(Guid id, CancellationToken ct)
    {
        var j = await db.LabelPrintJobs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Print job");
        if (!await m.CanAsync(Permissions.InventoryView, j.SiteId, j.DepartmentId, ct) && !await m.CanAsync(Permissions.ShippingView, j.SiteId, j.DepartmentId, ct)) throw new NotFoundException("Print job");
        return j;
    }

    /// <summary>The job's label as PDF (manual fallback), sized to its printer's stock or 4 x 6 in.</summary>
    public async Task<byte[]> JobPdfAsync(Guid id, CancellationToken ct)
    {
        var j = await ViewJobAsync(id, ct);
        var p = j.PrinterId is { } pid ? await db.LabelPrinters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct) : null;
        return LabelRenderer.Pdf([(JsonSerializer.Deserialize<LabelData>(j.DataJson, Json)!, j.Copies)], p?.WidthMm ?? 101.6m, p?.HeightMm ?? 152.4m);
    }

    public async Task<string> JobZplAsync(Guid id, CancellationToken ct)
    {
        var j = await ViewJobAsync(id, ct);
        var p = j.PrinterId is { } pid ? await db.LabelPrinters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct) : null;
        return LabelRenderer.Zpl(JsonSerializer.Deserialize<LabelData>(j.DataJson, Json)!, p?.Dpi ?? 203, p?.WidthMm ?? 101.6m, p?.HeightMm ?? 152.4m, j.Copies);
    }

    /// <summary>Preview of the label(s) a reference would print, as PDF, with the routed printer's stock size.</summary>
    public async Task<byte[]> PreviewAsync(LabelType type, string refType, Guid refId, CancellationToken ct)
    {
        var labels = await BuildAsync(type, refType, refId, ct);
        foreach (var d in labels) await RequireViewAsync(d, ct);
        var first = labels.FirstOrDefault();
        var printer = first is null ? null : (await ResolveAsync(first.Type, first.SiteId, first.LocationId, Workstation, m.UserId, ct)).Printer;
        return LabelRenderer.Pdf(labels.Select(d => (d, 1)), printer?.WidthMm ?? 101.6m, printer?.HeightMm ?? 152.4m);
    }
}
