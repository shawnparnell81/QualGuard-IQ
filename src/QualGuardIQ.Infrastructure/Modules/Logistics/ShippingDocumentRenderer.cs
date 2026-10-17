using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Logistics;

// ---- layout (stored as JSON on the template revision) ----
public sealed record DocPage(string Size = "Letter", string Orientation = "portrait", decimal MarginMm = 15);
public sealed record DocHeader(string Title, string? Subtitle = null, bool ShowLogo = true, decimal LogoWidthMm = 40);
public sealed record DocFooter(string? Text = null, bool PageNumbers = true);
/// <summary>One block of the document. Which options apply depends on the type (see <see cref="ShippingDocumentLayouts.Sections"/>).</summary>
public sealed record DocSection(string Type, string? Title = null, List<string>? Columns = null, string? Text = null, string? Field = null, List<string>? Signatures = null, decimal? SizeMm = null);
public sealed record DocLayout(DocPage Page, DocHeader Header, DocFooter Footer, List<DocSection> Sections);

// ---- data snapshot (stored with every generated document) ----
public sealed record DocParty(string? Name, List<string> Lines);
public sealed record DocLine(int Line, string Part, string? Description, string? Revision, decimal Quantity, string? Uom, string? Lot);
public sealed record DocItem(string Part, string? Lot, string? Serial, decimal Quantity);
public sealed record DocContainer(string Number, string Kind, string? Parent, decimal? WeightKg, decimal? LengthCm, decimal? WidthCm, decimal? HeightCm, List<DocItem> Items);
public sealed record DocManifestRow(string Shipment, string? Customer, string? ShipTo, string? Tracking, string? Bol, int Units, decimal? WeightKg);
public sealed record DocCustomer(string Code, string Name, string? Contact, string? Phone);
public sealed record DocCarrier(string Code, string Name, string? Scac, string? Service, string? Tracking);
public sealed record DocData(ShippingDocumentKind Kind, string Reference, string? ShipmentNumber, string? BolNumber, string? ShipmentKind, string? Status, string? CustomerPo,
    string? PromisedOn, string? ShipDate, string Site, DocParty Shipper, DocParty ShipTo, DocCustomer? Customer, DocCarrier? Carrier, decimal? GrossWeightKg,
    string? FreightTerms, string? Instructions, List<DocLine> Lines, List<DocContainer> Containers, List<DocManifestRow> Manifest, string GeneratedAt);

public sealed record SectionInfo(string Type, string Label, ShippingDocumentKind[] Kinds, string[]? Columns = null, string[]? Fields = null, bool UsesText = false, bool UsesSignatures = false, bool UsesSize = false);

/// <summary>Section catalogue, placeholders, validation and the built-in layouts.</summary>
public static class ShippingDocumentLayouts
{
    static readonly ShippingDocumentKind[] Shp = [ShippingDocumentKind.BillOfLading, ShippingDocumentKind.PackingList];
    static readonly ShippingDocumentKind[] All = [ShippingDocumentKind.BillOfLading, ShippingDocumentKind.PackingList, ShippingDocumentKind.ShippingManifest];
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly IReadOnlyList<SectionInfo> Sections =
    [
        new("addresses", "Shipper and ship-to addresses", All),
        new("customer", "Customer information", Shp),
        new("carrier", "Carrier information", All),
        new("shipment", "Shipment information", Shp),
        new("products", "Product table", Shp, Columns: ["line", "part", "description", "revision", "quantity", "uom", "lot"]),
        new("lots", "Lot table", Shp),
        new("serials", "Serial table", Shp),
        new("cartons", "Carton hierarchy (contents of every carton)", Shp),
        new("pallets", "Pallet hierarchy (cartons on each pallet)", Shp),
        new("weights", "Weights", All),
        new("dimensions", "Dimensions", Shp),
        new("freightTerms", "Freight terms", All, UsesText: true),
        new("instructions", "Special instructions", Shp, UsesText: true),
        new("signatures", "Signature blocks", All, UsesSignatures: true),
        new("barcode", "Barcode (Code 128)", All, Fields: ["shipmentNumber", "bolNumber", "trackingNumber", "customerPo", "manifestNumber"], UsesSize: true),
        new("qr", "QR code", All, UsesText: true, UsesSize: true),
        new("text", "Text", All, UsesText: true),
        new("manifestShipments", "Manifest shipment table", [ShippingDocumentKind.ShippingManifest], Columns: ["shipment", "customer", "shipTo", "tracking", "bol", "units", "weight"]),
    ];

    public static readonly IReadOnlyList<string> Placeholders = ["shipmentNumber", "bolNumber", "trackingNumber", "customerPo", "shipDate", "carrier", "customer", "site", "serviceLevel", "manifestNumber", "reference"];
    static readonly Regex Ph = new(@"\{(\w+)\}", RegexOptions.Compiled);

    public static DocLayout Parse(string json) => JsonSerializer.Deserialize<DocLayout>(json, Json) ?? throw new AppException("The layout is empty.");
    public static string Serialize(DocLayout l) => JsonSerializer.Serialize(l, Json);

    /// <summary>All problems with a layout for a document kind, in reading order.</summary>
    public static List<string> Validate(DocLayout? l, ShippingDocumentKind kind)
    {
        var e = new List<string>();
        if (l is null) return ["The layout is empty."];
        if (l.Page is null || l.Header is null || l.Footer is null || l.Sections is null) return ["The layout needs page, header, footer and sections."];
        if (l.Page.Size is not ("Letter" or "A4")) e.Add("Page size must be Letter or A4.");
        if (l.Page.Orientation is not ("portrait" or "landscape")) e.Add("Orientation must be portrait or landscape.");
        if (l.Page.MarginMm is < 5 or > 40) e.Add("Margins must be 5–40 mm.");
        if (string.IsNullOrWhiteSpace(l.Header.Title) || l.Header.Title.Length > 120) e.Add("The header title is required (up to 120 characters).");
        if (l.Header.Subtitle?.Length > 200) e.Add("The subtitle is longer than 200 characters.");
        if (l.Header.LogoWidthMm is < 10 or > 80) e.Add("Logo width must be 10–80 mm.");
        if (l.Footer.Text?.Length > 300) e.Add("The footer text is longer than 300 characters.");
        Text(l.Header.Title, "Header title"); Text(l.Header.Subtitle, "Subtitle"); Text(l.Footer.Text, "Footer");
        if (l.Sections.Count == 0) e.Add("Add at least one section.");
        if (l.Sections.Count > 40) e.Add("A layout can have at most 40 sections.");
        for (var i = 0; i < l.Sections.Count; i++)
        {
            var s = l.Sections[i]; var n = $"Section {i + 1}";
            var info = Sections.FirstOrDefault(x => x.Type == s.Type);
            if (info is null) { e.Add($"{n}: unknown section type '{s.Type}'."); continue; }
            n = $"Section {i + 1} ({info.Label})";
            if (!info.Kinds.Contains(kind)) { e.Add($"{n} is not available on a {Label(kind)}."); continue; }
            if (s.Title?.Length > 120) e.Add($"{n}: the title is longer than 120 characters.");
            if (s.Columns is { Count: > 0 } cols)
            {
                if (info.Columns is null) e.Add($"{n} has no columns to choose.");
                else foreach (var c in cols.Where(c => !info.Columns.Contains(c))) e.Add($"{n}: unknown column '{c}'.");
                if (cols.Distinct().Count() != cols.Count) e.Add($"{n}: a column is listed twice.");
            }
            if (info.Fields is not null && (s.Field is null || !info.Fields.Contains(s.Field))) e.Add($"{n}: choose the value to encode ({string.Join(", ", info.Fields)}).");
            if (s.Type == "qr" && string.IsNullOrWhiteSpace(s.Text)) e.Add($"{n}: enter the QR content (placeholders such as {{shipmentNumber}} are allowed).");
            if (s.Type == "text" && string.IsNullOrWhiteSpace(s.Text)) e.Add($"{n}: enter the text.");
            if (s.Text?.Length > 2000) e.Add($"{n}: the text is longer than 2,000 characters.");
            Text(s.Text, n); Text(s.Title, n);
            if (info.UsesSignatures)
            {
                if (s.Signatures is not { Count: > 0 and <= 4 }) e.Add($"{n}: list 1–4 signature blocks.");
                else if (s.Signatures.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 60)) e.Add($"{n}: each signature label is required (up to 60 characters).");
            }
            if (s.SizeMm is { } sz && (sz < 10 || sz > 120)) e.Add($"{n}: size must be 10–120 mm.");
        }
        return e;

        void Text(string? t, string where)
        {
            if (t is null) return;
            foreach (Match mt in Ph.Matches(t))
                if (!Placeholders.Contains(mt.Groups[1].Value)) e.Add($"{where}: unknown placeholder {{{mt.Groups[1].Value}}}. Use {string.Join(", ", Placeholders.Select(p => "{" + p + "}"))}.");
        }
    }

    public static string Label(ShippingDocumentKind k) => k switch { ShippingDocumentKind.BillOfLading => "bill of lading", ShippingDocumentKind.PackingList => "packing list", _ => "shipping manifest" };

    /// <summary>The layout used when no approved template applies; also the starting point of a new template.</summary>
    public static DocLayout BuiltIn(ShippingDocumentKind k) => k switch
    {
        ShippingDocumentKind.BillOfLading => new(new(), new("Bill of lading {bolNumber}", "Shipment {shipmentNumber}"), new("Received in good order, subject to the classifications and tariffs in effect on the date of issue."),
            [new("barcode", Field: "bolNumber"), new("addresses"), new("carrier"), new("shipment"), new("freightTerms"), new("products", Columns: ["line", "part", "description", "revision", "quantity", "uom"]),
             new("weights"), new("instructions"), new("signatures", Signatures: ["Shipper", "Carrier", "Consignee"])]),
        ShippingDocumentKind.PackingList => new(new(), new("Packing list {shipmentNumber}"), new(),
            [new("addresses"), new("customer"), new("shipment"), new("products", Columns: ["line", "part", "description", "revision", "quantity", "uom"]), new("cartons"), new("pallets"), new("lots"), new("serials"), new("weights"), new("instructions")]),
        _ => new(new(Orientation: "landscape"), new("Shipping manifest {manifestNumber}", "{carrier} · {shipDate}"), new(),
            [new("barcode", Field: "manifestNumber"), new("addresses"), new("carrier"), new("manifestShipments", Columns: ["shipment", "customer", "shipTo", "tracking", "bol", "units", "weight"]), new("weights"),
             new("signatures", Signatures: ["Shipper", "Carrier driver"])]),
    };

    public static string Fill(string? t, DocData d) => t is null ? "" : Ph.Replace(t, mt => mt.Groups[1].Value switch
    {
        "shipmentNumber" => d.ShipmentNumber ?? "", "bolNumber" => d.BolNumber ?? "(issued at shipping)", "trackingNumber" => d.Carrier?.Tracking ?? "", "customerPo" => d.CustomerPo ?? "",
        "shipDate" => d.ShipDate ?? "", "carrier" => d.Carrier?.Name ?? "", "customer" => d.Customer?.Name ?? "", "site" => d.Site, "serviceLevel" => d.Carrier?.Service ?? "",
        "manifestNumber" => d.Kind == ShippingDocumentKind.ShippingManifest ? d.Reference : "", "reference" => d.Reference, _ => mt.Value,
    });

    /// <summary>Sample data for previews without a shipment.</summary>
    public static DocData Sample(ShippingDocumentKind k)
    {
        var items1 = new List<DocItem> { new("PN-1001", "LOT-2401", null, 40), new("PN-1002", "LOT-2402", null, 10) };
        var items2 = new List<DocItem> { new("PN-2001", null, "SN-0001", 1), new("PN-2001", null, "SN-0002", 1) };
        return new(k, k == ShippingDocumentKind.ShippingManifest ? "MAN-SAMPLE-20260101" : "SHP-SAMPLE", "SHP-SAMPLE", "BOL-SAMPLE", "Customer", "Packed", "PO-12345", "2026-01-05", "2026-01-04", "Sample Plant",
            new("Sample Plant", ["100 Industrial Way", "Dayton OH 45402", "US"]), new("Sample Customer Receiving", ["12 Harbor Rd", "Toledo OH 43604", "US"]),
            new("CUST-1", "Sample Customer", "Receiving desk", "+1 555 0100"), new("SAMPLE", "Sample Freight", "SMPL", "Ground", "TRK-SAMPLE"), 63.5m, "Prepaid",
            "Deliver to dock 4. Do not stack.",
            [new(1, "PN-1001", "Pump cover", "A", 40, "EA", "LOT-2401"), new(2, "PN-1002", "Gasket", "C", 10, "EA", "LOT-2402"), new(3, "PN-2001", "Controller", "B", 2, "EA", null)],
            [new("PAL-0001", "Pallet", null, 60, 120, 100, 110, []), new("PKG-0001", "Carton", "PAL-0001", 40.5m, 60, 40, 30, items1), new("PKG-0002", "Carton", "PAL-0001", 3m, 40, 30, 20, items2)],
            k == ShippingDocumentKind.ShippingManifest ? [new("SHP-SAMPLE", "Sample Customer", "Toledo OH", "TRK-SAMPLE", "BOL-SAMPLE", 1, 63.5m), new("SHP-SAMPLE2", "Other Customer", "Detroit MI", "TRK-SAMPLE2", "BOL-SAMPLE2", 2, 12m)] : [],
            "2026-01-04 08:00");
    }
}

/// <summary>Renders a layout and a data snapshot to PDF. Deterministic: the same layout, logo and snapshot always give the same bytes.</summary>
public static class ShippingDocumentRenderer
{
    const double Mm = 72 / 25.4;
    static string N(decimal v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static string? Kg(decimal? v) => v is { } x ? N(x) + " kg" : null;

    public static (byte[] Pdf, int Pages) Render(DocLayout l, DocData d, byte[]? logo, string? watermark = null)
    {
        var first = Draw(l, d, logo, watermark, null);
        return (Draw(l, d, logo, watermark, first.PageCount).ToBytes(), first.PageCount);
    }

    sealed class Ctx(PdfCanvas p, double w, double h, double m, Action newPage)
    {
        public PdfCanvas P = p; public double W = w, H = h, M = m, Y; public double Bottom => H - M - 22;
        public double Inner => W - 2 * M;
        public void Need(double height) { if (Y + height > Bottom) newPage(); }
    }

    static PdfCanvas Draw(DocLayout l, DocData d, byte[]? logo, string? watermark, int? total)
    {
        double w = l.Page.Size == "A4" ? 595.28 : 612, h = l.Page.Size == "A4" ? 841.89 : 792;
        if (l.Page.Orientation == "landscape") (w, h) = (h, w);
        var m = (double)l.Page.MarginMm * Mm;
        var pdf = new PdfCanvas(w, h);
        var page = 1;
        Ctx c = null!;
        void Header()
        {
            var top = m; var logoH = 0.0;
            if (l.Header.ShowLogo && logo is { Length: > 0 } && PdfCanvas.JpegSize(logo) is { } sz)
            {
                var lw = (double)l.Header.LogoWidthMm * Mm; logoH = Math.Min(lw * sz.H / sz.W, 22 * Mm);
                pdf.Jpeg(w - m - lw, top, lw, logoH, logo);
            }
            else if (l.Header.ShowLogo)
            {
                // no logo uploaded to the template (or a built-in layout): the QualGuard IQ logo is the default header
                logoH = Math.Min(BrandLogo.HeightForWidth((double)l.Header.LogoWidthMm * Mm), 22 * Mm);
                pdf.Logo(w - m - BrandLogo.WidthForHeight(logoH), top, logoH);
            }
            var tw = w - 2 * m - (logoH > 0 ? (double)l.Header.LogoWidthMm * Mm + 10 : 0);
            pdf.TextFit(m, top, 16, tw, ShippingDocumentLayouts.Fill(l.Header.Title, d), bold: true);
            var y = top + 20;
            if (!string.IsNullOrWhiteSpace(l.Header.Subtitle)) { pdf.TextFit(m, y, 10, tw, ShippingDocumentLayouts.Fill(l.Header.Subtitle, d)); y += 14; }
            if (watermark is not null) { pdf.Rect(m, y + 2, tw, 16, width: 1.5); pdf.TextFit(m + 4, y + 5, 9, tw - 8, watermark, bold: true); y += 22; }
            y = Math.Max(y, top + logoH) + 6;
            pdf.Line(m, y, w - m, y, 1);
            c.Y = y + 10;
            // footer
            var fy = h - m - 12;
            pdf.Line(m, fy - 4, w - m, fy - 4);
            if (!string.IsNullOrWhiteSpace(l.Footer.Text)) pdf.TextFit(m, fy, 7.5, w - 2 * m - 90, ShippingDocumentLayouts.Fill(l.Footer.Text, d));
            if (l.Footer.PageNumbers) { var t = total is { } n ? $"Page {page} of {n}" : $"Page {page}"; pdf.Text(w - m - PdfCanvas.Width(t, 7.5), fy, 7.5, t); }
        }
        c = new Ctx(pdf, w, h, m, () => { pdf.AddPage(w, h); page++; Header(); });
        Header();
        foreach (var s in l.Sections) Section(c, s, d);
        return pdf;
    }

    static void Heading(Ctx c, string text) { c.Need(30); c.P.Text(c.M, c.Y, 10.5, text, bold: true); c.Y += 15; }
    static void Fields(Ctx c, IEnumerable<(string Label, string? Value)> f)
    {
        foreach (var (k, v) in f.Where(x => !string.IsNullOrWhiteSpace(x.Value)))
        {
            var lines = PdfCanvas.Wrap(v!, 9, c.Inner - 130).ToList();
            c.Need(12 * lines.Count);
            c.P.TextFit(c.M, c.Y, 9, 125, k, bold: true);
            foreach (var ln in lines) { c.P.Text(c.M + 130, c.Y, 9, ln); c.Y += 12; }
        }
        c.Y += 6;
    }

    static void Table(Ctx c, (string Name, double Weight)[] cols, IEnumerable<string?[]> rows)
    {
        var tw = cols.Sum(x => x.Weight); var widths = cols.Select(x => c.Inner * x.Weight / tw).ToArray();
        void Head()
        {
            c.Need(30); var x = c.M;
            c.P.Rect(c.M, c.Y - 2, c.Inner, 14, width: 0.6);
            for (var i = 0; i < cols.Length; i++) { c.P.TextFit(x + 3, c.Y + 1, 8.5, widths[i] - 6, cols[i].Name, bold: true); x += widths[i]; }
            c.Y += 15;
        }
        Head();
        var any = false;
        foreach (var r in rows)
        {
            any = true;
            if (c.Y + 12 > c.Bottom) { c.Need(c.Bottom); Head(); }
            var x = c.M;
            for (var i = 0; i < cols.Length; i++) { c.P.TextFit(x + 3, c.Y, 8.5, widths[i] - 6, r[i] ?? ""); x += widths[i]; }
            c.Y += 12;
        }
        if (!any) { c.P.Text(c.M + 3, c.Y, 8.5, "None."); c.Y += 12; }
        c.Y += 8;
    }

    static string Dims(DocContainer x) => x.LengthCm is { } a && x.WidthCm is { } b && x.HeightCm is { } hh ? $"{N(a)} × {N(b)} × {N(hh)} cm" : "";

    static void Section(Ctx c, DocSection s, DocData d)
    {
        var info = ShippingDocumentLayouts.Sections.First(x => x.Type == s.Type);
        string T(string fallback) => string.IsNullOrWhiteSpace(s.Title) ? fallback : ShippingDocumentLayouts.Fill(s.Title, d);
        var top = d.Containers.Where(x => x.Parent is null).ToList();
        switch (s.Type)
        {
            case "addresses":
            {
                c.Need(80);
                var half = c.Inner / 2; var y0 = c.Y;
                double Block(double x, string label, DocParty p)
                {
                    var y = y0; c.P.Text(x, y, 9, string.IsNullOrWhiteSpace(s.Title) ? label : label, bold: true); y += 13;
                    foreach (var ln in new[] { p.Name }.Concat(p.Lines).Where(z => !string.IsNullOrWhiteSpace(z))) { c.P.TextFit(x, y, 9, half - 12, ln!); y += 12; }
                    return y;
                }
                var a = Block(c.M, "Ship from (shipper)", d.Shipper);
                var b = d.Kind == ShippingDocumentKind.ShippingManifest ? a : Block(c.M + half, "Ship to (consignee)", d.ShipTo);
                c.Y = Math.Max(a, b) + 8;
                break;
            }
            case "customer":
                Heading(c, T("Customer"));
                Fields(c, [("Customer", d.Customer is { } cu ? $"{cu.Name} ({cu.Code})" : null), ("Contact", d.Customer?.Contact), ("Phone", d.Customer?.Phone), ("Customer PO", d.CustomerPo)]);
                break;
            case "carrier":
                Heading(c, T("Carrier"));
                Fields(c, [("Carrier", d.Carrier?.Name ?? "Not assigned"), ("SCAC", d.Carrier?.Scac), ("Service", d.Carrier?.Service), ("Tracking / PRO", d.Carrier?.Tracking)]);
                break;
            case "shipment":
                Heading(c, T("Shipment"));
                Fields(c, [("Shipment", d.ShipmentNumber), ("Bill of lading", d.BolNumber), ("Type", d.ShipmentKind), ("Customer PO", d.CustomerPo), ("Promised", d.PromisedOn), ("Ship date", d.ShipDate)]);
                break;
            case "products":
            {
                Heading(c, T("Products"));
                var cols = s.Columns is { Count: > 0 } x ? x : info.Columns!.ToList();
                (string, double) Col(string k) => k switch { "line" => ("Line", 0.6), "part" => ("Part", 2), "description" => ("Description", 3), "revision" => ("Rev", 0.7), "quantity" => ("Qty", 1), "uom" => ("UoM", 0.7), _ => ("Lot", 1.6) };
                string? V(DocLine ln, string k) => k switch { "line" => ln.Line.ToString(CultureInfo.InvariantCulture), "part" => ln.Part, "description" => ln.Description, "revision" => ln.Revision, "quantity" => N(ln.Quantity), "uom" => ln.Uom, _ => ln.Lot };
                Table(c, cols.Select(Col).ToArray(), d.Lines.Select(ln => cols.Select(k => V(ln, k)).ToArray()));
                break;
            }
            case "lots":
                Heading(c, T("Lots"));
                Table(c, [("Part", 2), ("Lot", 2), ("Quantity", 1)], d.Containers.SelectMany(x => x.Items).Where(i => i.Lot is not null).GroupBy(i => (i.Part, i.Lot))
                    .OrderBy(g => g.Key.Part, StringComparer.Ordinal).ThenBy(g => g.Key.Lot, StringComparer.Ordinal).Select(g => new[] { g.Key.Part, g.Key.Lot, N(g.Sum(i => i.Quantity)) }));
                break;
            case "serials":
                Heading(c, T("Serial numbers"));
                Table(c, [("Part", 2), ("Serial", 2), ("Container", 1.4)], d.Containers.SelectMany(x => x.Items.Where(i => i.Serial is not null).Select(i => new[] { i.Part, i.Serial, x.Number }))
                    .OrderBy(r => r[0], StringComparer.Ordinal).ThenBy(r => r[1], StringComparer.Ordinal));
                break;
            case "cartons":
            {
                Heading(c, T("Cartons"));
                var any = false;
                foreach (var x in d.Containers.Where(x => x.Kind != "Pallet"))
                {
                    any = true; c.Need(40);
                    c.P.TextFit(c.M, c.Y, 9, c.Inner, $"{x.Kind} {x.Number}{(x.Parent is { } p ? " on " + p : "")}{(x.WeightKg is { } wk ? " · " + N(wk) + " kg" : "")}{(Dims(x) is { Length: > 0 } dm ? " · " + dm : "")}", bold: true);
                    c.Y += 13;
                    if (x.Items.Count > 0) Table(c, [("Part", 2), ("Lot", 1.6), ("Serial", 1.6), ("Qty", 0.8)], x.Items.Select(i => new[] { i.Part, i.Lot, i.Serial, N(i.Quantity) }));
                }
                if (!any) { c.P.Text(c.M, c.Y, 8.5, "Not packed in cartons."); c.Y += 14; }
                break;
            }
            case "pallets":
            {
                var pallets = d.Containers.Where(x => x.Kind == "Pallet").ToList();
                if (pallets.Count == 0) break; // nothing to show on loose-carton shipments
                Heading(c, T("Pallets"));
                foreach (var p in pallets)
                {
                    c.Need(40);
                    c.P.TextFit(c.M, c.Y, 9, c.Inner, $"Pallet {p.Number}{(p.WeightKg is { } wk ? " · " + N(wk) + " kg" : "")}{(Dims(p) is { Length: > 0 } dm ? " · " + dm : "")}", bold: true);
                    c.Y += 13;
                    Table(c, [("Carton", 1.5), ("Weight", 1), ("Dimensions", 1.6), ("Items", 0.8)], d.Containers.Where(x => x.Parent == p.Number)
                        .Select(x => new[] { x.Number, Kg(x.WeightKg), Dims(x), N(x.Items.Sum(i => i.Quantity)) }));
                }
                break;
            }
            case "weights":
            {
                Heading(c, T("Weight"));
                if (d.Kind == ShippingDocumentKind.ShippingManifest)
                    Fields(c, [("Shipments", d.Manifest.Count.ToString(CultureInfo.InvariantCulture)), ("Handling units", d.Manifest.Sum(r => r.Units).ToString(CultureInfo.InvariantCulture)),
                        ("Total weight", Kg(d.Manifest.All(r => r.WeightKg is null) ? null : d.Manifest.Sum(r => r.WeightKg ?? 0)))]);
                else
                    Fields(c, [("Gross weight", Kg(d.GrossWeightKg)), ("Packed weight", Kg(top.All(x => x.WeightKg is null) ? null : top.Sum(x => x.WeightKg ?? 0))),
                        ("Handling units", top.Count == 0 ? null : $"{top.Count} ({string.Join(", ", top.Select(x => x.Kind + " " + x.Number))})")]);
                break;
            }
            case "dimensions":
                Heading(c, T("Dimensions"));
                Table(c, [("Container", 1.4), ("Type", 1), ("Length", 1), ("Width", 1), ("Height", 1), ("Weight", 1)],
                    top.Select(x => new[] { x.Number, x.Kind, x.LengthCm is { } a ? N(a) + " cm" : "", x.WidthCm is { } b ? N(b) + " cm" : "", x.HeightCm is { } hh ? N(hh) + " cm" : "", Kg(x.WeightKg) }));
                break;
            case "freightTerms":
                Heading(c, T("Freight terms"));
                Fields(c, [("Terms", d.FreightTerms ?? (string.IsNullOrWhiteSpace(s.Text) ? "Not stated" : ShippingDocumentLayouts.Fill(s.Text, d)))]);
                break;
            case "instructions":
            {
                var txt = string.Join(" ", new[] { string.IsNullOrWhiteSpace(s.Text) ? null : ShippingDocumentLayouts.Fill(s.Text, d), d.Instructions }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (txt.Length == 0) break;
                Heading(c, T("Special instructions"));
                c.Need(14); c.Y += c.P.TextBox(c.M, c.Y, 9, c.Inner, txt) + 6;
                break;
            }
            case "signatures":
            {
                var sigs = s.Signatures ?? ["Signature"];
                c.Need(64);
                if (!string.IsNullOrWhiteSpace(s.Title)) { c.P.Text(c.M, c.Y, 10.5, T(""), bold: true); c.Y += 15; }
                var bw = c.Inner / sigs.Count;
                for (var i = 0; i < sigs.Count; i++)
                {
                    var x = c.M + i * bw;
                    c.P.Line(x, c.Y + 28, x + bw - 14, c.Y + 28);
                    c.P.TextFit(x, c.Y + 32, 8, bw - 14, $"{sigs[i]} signature");
                    c.P.Line(x, c.Y + 56, x + (bw - 14) * 0.6, c.Y + 56);
                    c.P.Text(x, c.Y + 60, 8, "Date");
                }
                c.Y += 78;
                break;
            }
            case "barcode":
            {
                var v = s.Field switch { "shipmentNumber" => d.ShipmentNumber, "bolNumber" => d.BolNumber, "trackingNumber" => d.Carrier?.Tracking, "customerPo" => d.CustomerPo, _ => d.Kind == ShippingDocumentKind.ShippingManifest ? d.Reference : null };
                if (string.IsNullOrWhiteSpace(v)) break; // e.g. the BOL number before shipping
                var bw = Math.Min(c.Inner, (double)(s.SizeMm ?? 70) * Mm); c.Need(52);
                c.P.Modules(c.M, c.Y, bw, 36, LabelRenderer.Matrix(LabelSymbology.Code128, v));
                c.P.TextFit(c.M, c.Y + 39, 8, bw, v); c.Y += 56;
                break;
            }
            case "qr":
            {
                var v = ShippingDocumentLayouts.Fill(s.Text, d);
                if (string.IsNullOrWhiteSpace(v)) break;
                var sz = (double)(s.SizeMm ?? 25) * Mm; c.Need(sz + 8);
                c.P.Modules(c.M, c.Y, sz, sz, LabelRenderer.Matrix(LabelSymbology.Qr, v));
                if (!string.IsNullOrWhiteSpace(s.Title)) c.P.TextFit(c.M + sz + 10, c.Y, 9, c.Inner - sz - 10, T(""), bold: true);
                c.Y += sz + 8;
                break;
            }
            case "text":
                if (!string.IsNullOrWhiteSpace(s.Title)) Heading(c, T(""));
                c.Need(14); c.Y += c.P.TextBox(c.M, c.Y, 9, c.Inner, ShippingDocumentLayouts.Fill(s.Text, d)) + 6;
                break;
            case "manifestShipments":
            {
                Heading(c, T("Shipments"));
                var cols = s.Columns is { Count: > 0 } x ? x : info.Columns!.ToList();
                (string, double) Col(string k) => k switch { "shipment" => ("Shipment", 1.3), "customer" => ("Customer", 2), "shipTo" => ("Ship to", 2), "tracking" => ("Tracking / PRO", 1.6), "bol" => ("BOL", 1.3), "units" => ("Units", 0.6), _ => ("Weight", 0.9) };
                string? V(DocManifestRow r, string k) => k switch { "shipment" => r.Shipment, "customer" => r.Customer, "shipTo" => r.ShipTo, "tracking" => r.Tracking, "bol" => r.Bol, "units" => r.Units.ToString(CultureInfo.InvariantCulture), _ => Kg(r.WeightKg) };
                Table(c, cols.Select(Col).ToArray(), d.Manifest.Select(r => cols.Select(k => V(r, k)).ToArray()));
                break;
            }
        }
    }
}
