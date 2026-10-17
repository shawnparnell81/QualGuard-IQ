using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace QualGuardIQ.Infrastructure.Modules.Traceability;

public enum BarcodeSymbology { Code128 = 0, Code39 = 1, DataMatrix = 2, Qr = 3 }
public sealed record ScanResult(string Raw, string Kind, string? Type, Guid? Id, string? Number, string? Url, string? Detail, List<ScanResult>? Candidates = null);

/// <summary>
/// Barcode and QR label rendering (SVG via ZXing) and scan resolution. Labels encode a typed prefix — "QG:LOT:LOT-000123",
/// "QG:SN:&lt;part&gt;:&lt;serial&gt;", "QG:LOC:…", "QG:WO:…", "QG:PO:…", "QG:REC:&lt;type&gt;:&lt;id&gt;" — so a wedge or camera scan
/// opens the right record. Plain values (supplier labels) are matched against lots, serials, work orders, POs and locations.
/// </summary>
public sealed class BarcodeService(AppDbContext db, ModuleSupport m)
{
    public static string Svg(BarcodeSymbology s, string content, int width = 0, int height = 0)
    {
        if (string.IsNullOrEmpty(content) || content.Length > 1000) throw new AppException("Barcode content must be 1–1000 characters.");
        if (s == BarcodeSymbology.Code39 && content.Any(c => !"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%".Contains(char.ToUpperInvariant(c))))
            throw new AppException("Code 39 supports A–Z, 0–9 and - . space $ / + % only.");
        var two = s is BarcodeSymbology.DataMatrix or BarcodeSymbology.Qr;
        var writer = new BarcodeWriterSvg
        {
            Format = s switch { BarcodeSymbology.Code128 => BarcodeFormat.CODE_128, BarcodeSymbology.Code39 => BarcodeFormat.CODE_39, BarcodeSymbology.DataMatrix => BarcodeFormat.DATA_MATRIX, _ => BarcodeFormat.QR_CODE },
            Options = new EncodingOptions { Width = width > 0 ? width : two ? 160 : 320, Height = height > 0 ? height : two ? 160 : 80, Margin = 2, PureBarcode = !two },
        };
        return writer.Write(s == BarcodeSymbology.Code39 ? content.ToUpperInvariant() : content).Content;
    }

    public async Task<ScanResult> ResolveAsync(string raw, CancellationToken ct)
    {
        var v = (raw ?? "").Trim().Trim('\r', '\n', '\u001d');
        if (v.Length == 0) throw new AppException("Nothing was scanned.");
        var parts = v.Split(':');
        if (parts.Length >= 3 && parts[0] == "QG")
        {
            var key = string.Join(':', parts.Skip(2));
            return parts[1] switch
            {
                "LOT" => await LotAsync(v, key, ct) ?? NotFound(v),
                "SN" when parts.Length >= 4 => await SerialAsync(v, parts[3], parts[2], ct) ?? NotFound(v),
                "SN" => await SerialAsync(v, key, null, ct) ?? NotFound(v),
                "WO" => await WoAsync(v, key, ct) ?? NotFound(v),
                "PO" => await PoAsync(v, key, ct) ?? NotFound(v),
                "LOC" => await LocAsync(v, key, ct) ?? NotFound(v),
                "REC" when parts.Length >= 4 && Guid.TryParse(parts[3], out var id) => new ScanResult(v, "Record", parts[2], id, null, null, null),
                _ => NotFound(v)
            };
        }
        // GS1 element strings: (10) batch/lot, (21) serial, (01) GTIN. Accept the human-readable form.
        if (v.StartsWith("(01)") || v.StartsWith("(10)") || v.StartsWith("(21)"))
        {
            var appIds = System.Text.RegularExpressions.Regex.Matches(v, "\\((\\d{2})\\)([^(]+)").ToDictionary(x => x.Groups[1].Value, x => x.Groups[2].Value);
            if (appIds.TryGetValue("21", out var sn) && await SerialAsync(v, sn, null, ct) is { } s) return s;
            if (appIds.TryGetValue("10", out var lot) && await LotAsync(v, lot, ct) is { } l) return l;
        }
        var hits = new List<ScanResult>();
        foreach (var r in new[] { await LotAsync(v, v, ct), await SerialAsync(v, v, null, ct), await WoAsync(v, v, ct), await PoAsync(v, v, ct), await LocAsync(v, v, ct) }) if (r is not null) hits.Add(r);
        return hits.Count switch { 0 => NotFound(v), 1 => hits[0], _ => new ScanResult(v, "Ambiguous", null, null, null, null, $"{hits.Count} matches", hits) };
    }

    static ScanResult NotFound(string v) => new(v, "NotFound", null, null, null, null, "No record you can view matches this code.");

    async Task<ScanResult?> LotAsync(string raw, string number, CancellationToken ct)
    {
        if (await m.TryScopeAsync(db.Lots.AsNoTracking(), Permissions.TraceView, ct) is not { } q) return null;
        var l = await q.FirstOrDefaultAsync(x => x.Number == number || x.SupplierLotNumber == number, ct);
        return l is null ? null : new ScanResult(raw, "Lot", "Lot", l.Id, l.Number, $"/trace/lots/{l.Id}", $"{l.Quantity:0.##} {l.UnitOfMeasure} · {l.Status}");
    }

    async Task<ScanResult?> SerialAsync(string raw, string serial, string? part, CancellationToken ct)
    {
        if (await m.TryScopeAsync(db.SerialUnits.AsNoTracking(), Permissions.TraceView, ct) is not { } q) return null;
        var s = part is null ? await q.Where(x => x.SerialNumber == serial).ToListAsync(ct)
            : await q.Where(x => x.SerialNumber == serial && db.Parts.Any(p => p.Id == x.PartId && p.PartNumber == part)).ToListAsync(ct);
        return s.Count != 1 ? null : new ScanResult(raw, "Serial", "SerialUnit", s[0].Id, s[0].SerialNumber, $"/trace/serials/{s[0].Id}", s[0].Status.ToString());
    }

    async Task<ScanResult?> WoAsync(string raw, string number, CancellationToken ct)
    {
        if (await m.TryScopeAsync(db.WorkOrders.AsNoTracking(), Permissions.ProductionView, ct) is not { } q) return null;
        var w = await q.FirstOrDefaultAsync(x => x.Number == number, ct);
        return w is null ? null : new ScanResult(raw, "WorkOrder", "WorkOrder", w.Id, w.Number, $"/production/work-orders/{w.Id}", $"{w.ItemNumber} · {w.Status}");
    }

    async Task<ScanResult?> PoAsync(string raw, string number, CancellationToken ct)
    {
        if (await m.TryScopeAsync(db.PurchaseOrders.AsNoTracking(), Permissions.ProductionView, ct) is not { } q) return null;
        var p = await q.FirstOrDefaultAsync(x => x.Number == number, ct);
        return p is null ? null : new ScanResult(raw, "PurchaseOrder", "PurchaseOrder", p.Id, p.Number, $"/production/purchase-orders/{p.Id}", p.Status.ToString());
    }

    // Warehouse locations arrive with the inventory module; resolution is extended there.
    public Func<string, string, CancellationToken, Task<ScanResult?>>? LocationResolver { get; set; }
    Task<ScanResult?> LocAsync(string raw, string code, CancellationToken ct) => LocationResolver?.Invoke(raw, code, ct) ?? Task.FromResult<ScanResult?>(null);
}
