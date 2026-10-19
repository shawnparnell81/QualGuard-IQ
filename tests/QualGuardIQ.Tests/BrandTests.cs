using System.Text;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Modules.Logistics;
using QualGuardIQ.Infrastructure.Modules.Reports;
using QualGuardIQ.Infrastructure.Services;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>The logo in generated documents: vector paths in PDFs (customer template logos first) and the HTML email view.</summary>
public class BrandTests
{
    // brand blue #1257C4 (where the mark's gradient starts), green #0E8550 (the dot) and charcoal #29333E as PDF fill colours
    const string Blue = "0.0706 0.3412 0.7686 rg", Green = "0.0549 0.5216 0.3137 rg", Charcoal = "0.1608 0.2 0.2431 rg";
    // the ring is clipped once per logo with the even-odd rule before its gradient bands are painted
    const string RingClip = "W* n";

    static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    /// <summary>Set QGIQ_BRAND_PDF_DIR to keep the generated PDFs for a visual check.</summary>
    static void Keep(string name, byte[] pdf)
    {
        if (Environment.GetEnvironmentVariable("QGIQ_BRAND_PDF_DIR") is { Length: > 0 } dir) { Directory.CreateDirectory(dir); File.WriteAllBytes(Path.Combine(dir, name), pdf); }
    }

    [Fact]
    public void Logo_operators_scale_to_the_requested_box()
    {
        var w = BrandLogo.WidthForHeight(20);
        Assert.InRange(w, 60, 90); // mark + wordmark is about 3.7 times as wide as it is tall
        Assert.Equal(20, BrandLogo.HeightForWidth(w), 6);
        var ops = BrandLogo.PdfOps(50, 752, 20);
        Assert.StartsWith("q ", ops);
        Assert.EndsWith("Q\n", ops);
        Assert.Contains(Blue, ops);
        Assert.Contains(Green, ops);
        Assert.Contains(Charcoal, ops);
        Assert.Equal(1, CountOf(ops, RingClip));
        Assert.Equal(ops.Count(c => c == 'q'), ops.Count(c => c == 'Q')); // balanced graphics state
    }

    [Fact]
    public void Record_pdfs_carry_the_logo_on_every_page()
    {
        var pdf = new SimplePdf("Test footer").Title("Inspection record");
        for (var i = 0; i < 120; i++) pdf.Line($"Line {i}");
        var bytes = pdf.ToBytes();
        Keep("record.pdf", bytes);
        var text = Text(bytes);
        var pages = text.Split("/Type /Page ").Length - 1;
        Assert.True(pages >= 2);
        Assert.Equal(pages, CountOf(text, RingClip)); // the ring: once per logo
    }

    [Fact]
    public void Report_pdfs_carry_the_logo()
    {
        var def = new ReportDefinition("t", "Test report", "Quality", "reports.view", "", true);
        var r = new ReportResult(def, new ReportParams(new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 17), null, null), [new ReportColumn("a", "Column A")],
            [new Dictionary<string, object?> { ["a"] = "x" }], null);
        var bytes = ReportService.ToPdf(r, "Test report");
        Keep("report.pdf", bytes);
        Assert.Contains(Blue, Text(bytes));
    }

    [Fact]
    public void Shipping_documents_use_the_template_logo_first_and_the_brand_logo_by_default()
    {
        var data = new DocData(ShippingDocumentKind.PackingList, "SHP-0001", "SHP-0001", null, "Customer", "Packed", "PO-1", null, "2026-10-17", "Plant 1",
            new DocParty("Plant 1", ["1 Main St"]), new DocParty("Customer", ["2 Side St"]), new DocCustomer("C-1", "Customer", null, null), null, 12m, null, null,
            [new DocLine(1, "100-2201", "Valve body", "C", 10, "EA", "L-1")], [], [], "2026-10-17 12:00");
        var layout = ShippingDocumentLayouts.BuiltIn(ShippingDocumentKind.PackingList);

        var (builtIn, _) = ShippingDocumentRenderer.Render(layout, data, null);
        Keep("packing-list.pdf", builtIn);
        Assert.Contains(Blue, Text(builtIn));

        // a customer JPEG on the template wins: the brand paths are not drawn
        var (custom, _) = ShippingDocumentRenderer.Render(layout, data, Jpeg());
        Assert.Contains("/DCTDecode", Text(custom));
        Assert.DoesNotContain(Blue, Text(custom));

        // logo switched off on the template: neither logo
        var (none, _) = ShippingDocumentRenderer.Render(layout with { Header = layout.Header with { ShowLogo = false } }, data, null);
        Assert.DoesNotContain(Blue, Text(none));
    }

    [Fact]
    public void Notification_email_has_a_branded_html_view_with_the_inline_logo()
    {
        using var msg = BrandedEmail.Create("from@qms.test", "to@qms.test", "QualGuard IQ", "Approval needed <NCR-0001>", "Please sign NCR-0001 & review.", "https://qms.test/ncr/1");
        Assert.Equal("[QualGuard IQ] Approval needed <NCR-0001>", msg.Subject);
        Assert.Contains("https://qms.test/ncr/1", msg.Body); // plain text stays the body
        var html = Assert.Single(msg.AlternateViews, v => v.ContentType.MediaType == "text/html");
        var markup = new StreamReader(html.ContentStream).ReadToEnd();
        Assert.Contains("cid:" + BrandedEmail.LogoContentId, markup);
        Assert.Contains(BrandLogo.BlueHex, markup);
        Assert.Contains("Approval needed &lt;NCR-0001&gt;", markup); // escaped
        Assert.Contains("Please sign NCR-0001 &amp; review.", markup);
        var logo = Assert.Single(html.LinkedResources);
        Assert.Equal(BrandedEmail.LogoContentId, logo.ContentId);
        Assert.Equal("image/png", logo.ContentType.MediaType);

        BrandedEmail.AppendNote(msg, "The file was not attached.");
        Assert.EndsWith("The file was not attached.", msg.Body);
        var view = Assert.Single(msg.AlternateViews);
        Assert.Contains("The file was not attached.", new StreamReader(view.ContentStream).ReadToEnd());
        Assert.Single(view.LinkedResources);
    }

    static int CountOf(string s, string part) { var n = 0; for (var i = s.IndexOf(part, StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++; return n; }

    /// <summary>Smallest baseline JPEG header the renderer accepts (SOI + SOF0 with a 2x1 size).</summary>
    static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x01, 0x00, 0x02, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01, 0xFF, 0xD9];
}
