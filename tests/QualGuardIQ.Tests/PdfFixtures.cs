using System.Globalization;
using System.Text;

namespace QualGuardIQ.Tests;

/// <summary>
/// Writes small PDF files for tests (written here, not copied): pages with Helvetica text and lines, fillable fields (text, checkbox,
/// dropdown, radio, signature), image-only pages that behave like scans, and optional script actions for the security checks.
/// </summary>
public sealed class PdfBuilder
{
    sealed class Page { public double W = 612, H = 792; public StringBuilder Ops = new(); public List<string> Widgets = []; public bool Image; }
    readonly List<Page> pages = [];
    readonly List<(string Name, string Dict, int Page, List<string>? Kids)> fields = [];
    public bool Script { get; set; }
    public string? Title { get; set; }
    Page Cur => pages[^1];
    static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    public PdfBuilder NewPage(bool landscape = false) { pages.Add(landscape ? new Page { W = 792, H = 612 } : new Page()); return this; }
    /// <summary>Text with its top at <paramref name="top"/> points from the top of the page.</summary>
    public PdfBuilder Text(double x, double top, string text, double size = 10, bool bold = false)
    { Cur.Ops.Append($"BT /{(bold ? "F2" : "F1")} {F(size)} Tf {F(x)} {F(Cur.H - top - size)} Td ({Esc(text)}) Tj ET\n"); return this; }
    public PdfBuilder Line(double x1, double top1, double x2, double top2) { Cur.Ops.Append($"0.5 w {F(x1)} {F(Cur.H - top1)} m {F(x2)} {F(Cur.H - top2)} l S\n"); return this; }
    /// <summary>A full-page picture and no text: what a scanner produces.</summary>
    public PdfBuilder ScannedPage() { NewPage(); Cur.Image = true; Cur.Ops.Append($"q {F(Cur.W)} 0 0 {F(Cur.H)} 0 0 cm /Im1 Do Q\n"); return this; }

    string Rect(double x, double top, double w, double h) => $"[{F(x)} {F(Cur.H - top - h)} {F(x + w)} {F(Cur.H - top)}]";
    public PdfBuilder TextField(string name, double x, double top, double w, double h = 16, string? value = null, string? tooltip = null, bool multiline = false, bool required = false)
    {
        var ff = (multiline ? 4096 : 0) | (required ? 2 : 0);
        fields.Add((name, $"/FT /Tx /T ({Esc(name)}) {(tooltip is null ? "" : $"/TU ({Esc(tooltip)})")} {(value is null ? "" : $"/V ({Esc(value)})")} /Ff {ff} /Rect {Rect(x, top, w, h)} /DA (/F1 10 Tf 0 g)", pages.Count - 1, null));
        return this;
    }
    public PdfBuilder CheckBox(string name, double x, double top, bool on = false, string? tooltip = null)
    {
        fields.Add((name, $"/FT /Btn /T ({Esc(name)}) {(tooltip is null ? "" : $"/TU ({Esc(tooltip)})")} /V /{(on ? "Yes" : "Off")} /AS /{(on ? "Yes" : "Off")} /Rect {Rect(x, top, 10, 10)} /AP << /N << /Yes AP /Off AP >> >>", pages.Count - 1, null));
        return this;
    }
    public PdfBuilder Dropdown(string name, double x, double top, double w, string[] options, string? value = null, string? tooltip = null)
    {
        fields.Add((name, $"/FT /Ch /Ff 131072 /T ({Esc(name)}) {(tooltip is null ? "" : $"/TU ({Esc(tooltip)})")} /Opt [{string.Join(" ", options.Select(o => $"({Esc(o)})"))}] {(value is null ? "" : $"/V ({Esc(value)})")} /Rect {Rect(x, top, w, 16)} /DA (/F1 10 Tf 0 g)", pages.Count - 1, null));
        return this;
    }
    public PdfBuilder Radio(string name, (string Option, double X, double Top)[] buttons, string? selected = null, string? tooltip = null)
    {
        var kids = buttons.Select(b => $"/Rect {Rect(b.X, b.Top, 10, 10)} /AS /{(b.Option == selected ? b.Option : "Off")} /AP << /N << /{b.Option} AP /Off AP >> >>").ToList();
        fields.Add((name, $"/FT /Btn /Ff 49152 /T ({Esc(name)}) {(tooltip is null ? "" : $"/TU ({Esc(tooltip)})")} {(selected is null ? "/V /Off" : $"/V /{selected}")}", pages.Count - 1, kids));
        return this;
    }
    public PdfBuilder SignatureField(string name, double x, double top, double w) { fields.Add((name, $"/FT /Sig /T ({Esc(name)}) /Rect {Rect(x, top, w, 24)}", pages.Count - 1, null)); return this; }

    public byte[] Build()
    {
        var objs = new List<string>();
        int Add(string body) { objs.Add(body); return objs.Count; }
        int Reserve() { objs.Add(""); return objs.Count; }
        var catalog = Reserve(); var pagesId = Reserve();
        var f1 = Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        var f2 = Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        var ap = Add("<< /Type /XObject /Subtype /Form /BBox [0 0 10 10] /Length 0 >>\nstream\n\nendstream");
        // an 8 x 8 grey picture (uncompressed) for scanned pages
        var pix = string.Concat(Enumerable.Range(0, 64).Select(i => (i % 3 == 0 ? "40" : "E0")));
        var img = Add($"<< /Type /XObject /Subtype /Image /Width 8 /Height 8 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /ASCIIHexDecode /Length {pix.Length + 1} >>\nstream\n{pix}>\nendstream");
        var pageIds = new List<int>();
        var fieldIds = new List<int>();
        var pageObjIds = pages.Select(_ => Reserve()).ToList();
        for (var i = 0; i < pages.Count; i++)
        {
            var p = pages[i];
            var content = p.Ops.ToString();
            var cid = Add($"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}endstream");
            var annots = new List<int>();
            foreach (var (name, dict, pg, kids) in fields.Where(f => f.Page == i))
            {
                if (kids is null) { var id = Add($"<< /Type /Annot /Subtype /Widget /F 4 /P {pageObjIds[i]} 0 R {dict.Replace("AP >>", $"{ap} 0 R >>").Replace("AP /", $"{ap} 0 R /")} >>"); annots.Add(id); fieldIds.Add(id); }
                else
                {
                    var parent = Reserve();
                    var kidIds = kids.Select(k => { var id = Add($"<< /Type /Annot /Subtype /Widget /F 4 /P {pageObjIds[i]} 0 R /Parent {parent} 0 R {k.Replace("AP /", $"{ap} 0 R /").Replace("AP >>", $"{ap} 0 R >>")} >>"); annots.Add(id); return id; }).ToList();
                    objs[parent - 1] = $"<< {dict} /Kids [{string.Join(" ", kidIds.Select(k => $"{k} 0 R"))}] >>";
                    fieldIds.Add(parent);
                }
            }
            var res = $"<< /Font << /F1 {f1} 0 R /F2 {f2} 0 R >>{(p.Image ? $" /XObject << /Im1 {img} 0 R >>" : "")} >>";
            objs[pageObjIds[i] - 1] = $"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {F(p.W)} {F(p.H)}] /Resources {res} /Contents {cid} 0 R{(annots.Count > 0 ? $" /Annots [{string.Join(" ", annots.Select(a => $"{a} 0 R"))}]" : "")} >>";
            pageIds.Add(pageObjIds[i]);
        }
        objs[pagesId - 1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(p => $"{p} 0 R"))}] /Count {pageIds.Count} >>";
        var acro = fieldIds.Count > 0 ? $" /AcroForm << /Fields [{string.Join(" ", fieldIds.Select(f => $"{f} 0 R"))}] /NeedAppearances true /DR << /Font << /F1 {f1} 0 R >> >> >>" : "";
        var js = Script ? " /OpenAction << /S /JavaScript /JS (app.alert\\(1\\)) >>" : "";
        objs[catalog - 1] = $"<< /Type /Catalog /Pages {pagesId} 0 R{acro}{js} >>";
        var info = Title is null ? 0 : Add($"<< /Title ({Esc(Title)}) /Producer (Quality test fixtures) >>");

        var sb = new StringBuilder("%PDF-1.7\n%\u00e2\u00e3\u00cf\u00d3\n");
        var offsets = new List<int>();
        for (var i = 0; i < objs.Count; i++) { offsets.Add(Encoding.Latin1.GetByteCount(sb.ToString())); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var xref = Encoding.Latin1.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objs.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Count + 1} /Root {catalog} 0 R{(info > 0 ? $" /Info {info} 0 R" : "")} >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>A supplier deviation request as a fillable PDF: text fields (one with a tooltip), a dropdown, a radio group, checkboxes, a numbered line table and a signature field.</summary>
    public static byte[] FillableDeviationRequest(bool filled = false, bool script = false)
    {
        var b = new PdfBuilder { Title = "Supplier Deviation Request", Script = script }.NewPage();
        b.Text(72, 50, "Supplier Deviation Request", 18, bold: true);
        b.Text(72, 100, "Supplier:").TextField("supplier", 160, 96, 200, value: filled ? "Acme Castings" : null);
        b.Text(380, 100, "Request date:").TextField("request_date", 460, 96, 90, value: filled ? "2026-10-12" : null);
        b.Text(72, 130, "Part number:").TextField("part_no", 160, 126, 120, value: filled ? "PN-4471" : null, required: true);
        b.TextField("qty_affected", 460, 126, 90, tooltip: "Quantity affected", value: filled ? "250" : null);
        b.Text(72, 160, "Deviation type:").Dropdown("deviation_type", 160, 156, 150, ["Dimensional", "Material", "Process", "Cosmetic"], filled ? "Material" : null);
        b.Text(72, 190, "Duration:").Radio("duration", [("Temporary", 160, 190), ("Permanent", 260, 190)], filled ? "Temporary" : null);
        b.Text(174, 190, "Temporary").Text(274, 190, "Permanent");
        b.CheckBox("customer_approval", 72, 220, on: filled).Text(88, 220, "Customer approval required");
        b.Text(72, 260, "Item", bold: true).Text(140, 260, "Characteristic", bold: true).Text(360, 260, "Qty", bold: true);
        for (var r = 1; r <= 3; r++) { b.TextField($"Characteristic{r}", 140, 260 + r * 22, 200, value: filled && r == 1 ? "Bore diameter" : null); b.TextField($"Qty{r}", 360, 260 + r * 22, 60, value: filled && r == 1 ? "12" : null); b.Text(72, 264 + r * 22, r.ToString(CultureInfo.InvariantCulture)); }
        b.Text(72, 380, "Details:").TextField("details", 160, 376, 380, 48, multiline: true, value: filled ? "Casting porosity within customer limit." : null);
        b.Text(72, 450, "Approved by:").SignatureField("approver_signature", 160, 444, 200);
        return b.Build();
    }

    /// <summary>A printed inspection form with no fillable fields: heading, label lines, checkbox symbols, a table with numbered blank rows and a total, instructions and sign-off lines.</summary>
    public static byte[] PrintedInspectionForm()
    {
        var b = new PdfBuilder { Title = "Final Inspection Report" }.NewPage();
        b.Text(72, 50, "Final Inspection Report", 18, bold: true);
        b.Text(72, 90, "Work order: ____________").Text(330, 90, "Inspection date: __________");
        b.Text(72, 115, "Inspector: ____________");
        b.Text(72, 140, "Record every measurement in the table below. Use the calibrated gauge listed on the traveler.");
        b.Text(72, 175, "Measurements", 13, bold: true);
        b.Text(72, 200, "No.", bold: true).Text(130, 200, "Characteristic", bold: true).Text(330, 200, "Nominal", bold: true).Text(430, 200, "Measured", bold: true);
        for (var r = 1; r <= 3; r++) b.Text(72, 200 + r * 20, r.ToString(CultureInfo.InvariantCulture));
        b.Text(72, 290, "Total").Text(430, 290, "0.00");
        b.Text(72, 330, "Result:").Text(140, 330, "[ ] Accept").Text(240, 330, "[ ] Reject");
        b.Text(72, 380, "Approval", 13, bold: true);
        b.Text(72, 410, "Inspected by: ________________").Text(330, 410, "Date: __________");
        b.Text(72, 440, "Approved by: ________________").Text(330, 440, "Date: __________");
        return b.Build();
    }

    /// <summary>A two-page report whose second page lists results that can be extracted as rows.</summary>
    public static byte[] ResultsReport()
    {
        var b = new PdfBuilder { Title = "Torque audit" }.NewPage();
        b.Text(72, 50, "Torque audit", 18, bold: true).Text(72, 90, "Summary page: see the results on page 2.");
        b.NewPage();
        b.Text(72, 50, "Results", 14, bold: true);
        b.Text(72, 80, "Part number", bold: true).Text(230, 80, "Torque", bold: true).Text(330, 80, "Checked on", bold: true);
        b.Text(72, 100, "P-301").Text(230, 100, "41.5").Text(330, 100, "2026-10-08");
        b.Text(72, 120, "P-302").Text(230, 120, "44").Text(330, 120, "2026-10-09");
        b.Text(72, 140, "P-303").Text(230, 140, "39.75").Text(330, 140, "2026-10-10");
        return b.Build();
    }

    public static byte[] Scanned(int pages = 1) { var b = new PdfBuilder { Title = "Scanned form" }; for (var i = 0; i < pages; i++) b.ScannedPage(); return b.Build(); }
}
