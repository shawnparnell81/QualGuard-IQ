using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 15: shipping document templates (bill of lading, packing list, manifest) with revisions, approval, effective dates, selection rules and reproducible issued documents.</summary>
[Collection("api")]
public sealed class GapClosureShippingDocumentTests(TestApp app)
{
    static DateOnly Today => TestDates.Today;
    static readonly string[] Perms =
    [
        Permissions.InventoryView, Permissions.InventoryEdit, Permissions.ShippingView, Permissions.ShippingEdit, Permissions.MasterDataView, Permissions.MasterDataEdit,
        Permissions.TraceView, Permissions.TraceEdit, Permissions.InventoryReceiveBlind,
    ];
    static readonly byte[] Logo = Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAoHBwgHBgoICAgLCgoLDhgQDg0NDh0VFhEYIx8lJCIfIiEmKzcvJik0KSEiMEExNDk7Pj4+JS5ESUM8SDc9Pjv/2wBDAQoLCw4NDhwQEBw7KCIoOzs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozs7Ozv/wAARCAAQACgDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDiKKKK+uPJCiiigAooooA//9k=");

    sealed record World(Guid Site, ApiClient Lead, Guid LeadId, ApiClient Approver, ApiClient Viewer, string Tag, Dictionary<string, Guid> Loc, Guid Customer, Guid Carrier);

    async Task<World> SetupAsync()
    {
        var site = await app.CreateSiteAsync();
        var (leadId, lead) = await app.CreateUserAsync([.. Perms, Permissions.InventoryApprove], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.ShippingView, Permissions.ShippingApprove], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.ShippingView], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var loc = new Dictionary<string, Guid>();
        async Task L(string key, int kind, int purpose, string? parent) =>
            loc[key] = (await (await lead.PostOkAsync("/api/warehouse/locations", new { code = $"{key}-{tag}", name = key, kind, purpose, parentId = parent is null ? (Guid?)null : loc[parent], capacity = (decimal?)1000m, siteId = site })).JsonAsync()).Id();
        await L("WH", 0, 0, null); await L("RECV", 2, 1, "WH"); await L("A1", 6, 0, "WH"); await L("SHIP", 2, 6, "WH");
        var cust = (await (await lead.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Acme " + tag, siteId = site, status = 0 })).JsonAsync()).Id();
        var carrier = (await (await lead.PostOkAsync("/api/shipping/carriers", new { code = "DC" + tag, name = "Freight " + tag, kind = 0, trackingUrl = (string?)null, scac = "FRT" + tag[..1], active = true })).JsonAsync()).Id();
        return new World(site, lead, leadId, approver, viewer, tag, loc, cust, carrier);
    }

    static async Task<JsonElement> Expect(HttpStatusCode code, HttpResponseMessage r, string? contains = null)
    {
        var body = await r.Content.ReadAsStringAsync();
        if (r.StatusCode != code) Assert.Fail($"expected {(int)code}, got {(int)r.StatusCode} {r.RequestMessage?.RequestUri?.PathAndQuery}: {body}");
        if (contains is not null) Assert.Contains(contains, body);
        return body.Length > 0 && body[0] is '{' or '[' ? JsonDocument.Parse(body).RootElement.Clone() : default;
    }

    static async Task<(string Text, byte[] Bytes, Guid? DocId)> PdfAsync(ApiClient c, string url)
    {
        var r = await c.GetAsync(url);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        if (r.StatusCode != HttpStatusCode.OK) Assert.Fail($"{url}: {(int)r.StatusCode} {Encoding.UTF8.GetString(bytes)}");
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF-", Encoding.Latin1.GetString(bytes, 0, 5));
        return (Encoding.Latin1.GetString(bytes), bytes, r.Headers.TryGetValues("X-Document-Id", out var v) ? Guid.Parse(v.First()) : null);
    }

    static async Task<Guid> PartAsync(World w, string prefix) =>
        (await (await w.Lead.PostOkAsync("/api/master/parts", new { partNumber = prefix + w.Tag, name = prefix + " part", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = w.Site, status = 0 })).JsonAsync()).Id();

    static async Task StockAsync(World w, Guid partId, decimal qty, string lot)
    {
        var line = new { partId, revision = (string?)null, purchaseOrderLineId = (Guid?)null, quantityExpected = (decimal?)null, quantityReceived = qty, supplierLotNumber = (string?)null, lotNumber = lot, serialNumbers = (string[]?)null,
            inspectionRequired = (bool?)false, inspectionPlanId = (Guid?)null, aqlPlanId = (Guid?)null, aqlInspectionLevel = (string?)null, aqlLevel = (decimal?)null, putAwayLocationId = (Guid?)w.Loc["A1"], clientRequestId = (string?)null };
        await w.Lead.PostOkAsync("/api/warehouse/receipts", new { kind = 3, supplierId = (Guid?)null, purchaseOrderId = (Guid?)null, asnNumber = (string?)null, packingSlip = "PS-" + w.Tag, carrierId = (Guid?)null,
            trackingNumber = (string?)null, receivedOn = Today, receivingLocationId = w.Loc["RECV"], notes = (string?)null, supplierOverrideReason = (string?)null, siteId = w.Site, departmentId = (Guid?)null, lines = new[] { line } });
    }

    /// <summary>A picked and packed shipment in two cartons (4 + 2 pcs, 3.5 kg and 2 kg, with dimensions).</summary>
    async Task<(Guid Id, List<string> Cartons)> PackedAsync(World w)
    {
        var part = await PartAsync(w, "DT-");
        var lot = "DL-" + w.Tag;
        await StockAsync(w, part, 10m, lot);
        var shp = await (await w.Lead.PostOkAsync("/api/shipping/shipments", new { kind = 0, customerId = (Guid?)w.Customer, supplierId = (Guid?)null, toSiteId = (Guid?)null, shipTo = "Acme", customerPo = "PO-" + w.Tag,
            promisedOn = (DateOnly?)Today, stagingLocationId = w.Loc["SHIP"], carrierId = (Guid?)null, serviceLevel = (string?)null, notes = (string?)null, siteId = w.Site,
            lines = new[] { new { partId = part, revision = (string?)null, quantity = 6m, lotId = (Guid?)null } } })).JsonAsync();
        var id = shp.Id();
        var picks = (await (await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/pick-list")).JsonAsync()).EnumerateArray().ToList();
        var det = await w.Lead.GetElAsync($"/api/shipping/shipments/{id}");
        foreach (var p in det.GetProperty("picks").EnumerateArray())
            await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/picks/{p.GetProperty("task").Id()}/confirm",
                new { locationCode = p.Str("locationCode"), itemCode = p.Str("lotNumber"), quantity = p.GetProperty("task").GetProperty("quantity").GetDecimal() }));
        var lineId = det.GetProperty("lines")[0].GetProperty("line").Id();
        var lotId = picks[0].Id("lotId");
        var nos = new List<string>();
        foreach (var (q, kg) in new[] { (4m, 3.5m), (2m, 2m) })
        {
            var c = await (await w.Lead.PostOkAsync($"/api/shipping/shipments/{id}/containers", new { kind = 0, parentId = (Guid?)null, weight = (decimal?)kg, items = new[] { new { lineId, lotId = (Guid?)lotId, serialId = (Guid?)null, quantity = q } },
                lengthCm = (decimal?)40m, widthCm = (decimal?)30m, heightCm = (decimal?)20m })).JsonAsync();
            nos.Add(c.Str("number")!);
        }
        return (id, nos);
    }


    static object Layout(string title, string? footer = null, params object[] sections) =>
        new { page = new { size = "Letter", orientation = "portrait", marginMm = 15m }, header = new { title, subtitle = (string?)null, showLogo = true, logoWidthMm = 30m }, footer = new { text = footer, pageNumbers = true }, sections };
    static object Sec(string type, string? title = null, string[]? columns = null, string? text = null, string? field = null, string[]? signatures = null) =>
        new { type, title, columns, text, field, signatures, sizeMm = (decimal?)null };

    static object Rev(object layout, Guid? customerId = null, Guid? carrierId = null, int priority = 100, string? summary = "Layout for the customer", DateTimeOffset? effectiveFrom = null) =>
        new { layout, customerId, carrierId, priority, changeSummary = summary, effectiveFrom };

    static async Task<(Guid Template, Guid Revision)> TemplateAsync(ApiClient c, Guid? site, string code, int kind)
    {
        var t = await Expect(HttpStatusCode.OK, await c.PostAsync("/api/shipping/document-templates", new { code, name = code + " template", kind, description = (string?)null, siteId = site }));
        return (t.GetProperty("template").Id(), t.GetProperty("revisions")[0].Id());
    }

    static async Task ApprovedAsync(ApiClient editor, ApiClient approver, Guid rev, object revision)
    {
        await Expect(HttpStatusCode.NoContent, await editor.PutAsync($"/api/shipping/document-template-revisions/{rev}", revision));
        await Expect(HttpStatusCode.NoContent, await editor.PostAsync($"/api/shipping/document-template-revisions/{rev}/submit"));
        await Expect(HttpStatusCode.NoContent, await approver.PostAsync($"/api/shipping/document-template-revisions/{rev}/approve", new { notes = "Checked against the customer's routing guide.", effectiveFrom = (DateTimeOffset?)null }));
    }

    static async Task<HttpResponseMessage> UploadLogoAsync(ApiClient c, Guid rev, byte[] bytes, string name)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes); file.Headers.ContentType = new MediaTypeHeaderValue(name.EndsWith(".png") ? "image/png" : "image/jpeg");
        form.Add(file, "file", name);
        return await c.SendWithCsrfAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/shipping/document-template-revisions/{rev}/logo") { Content = form });
    }

    [Fact]
    public async Task A_template_revision_is_drafted_previewed_approved_by_someone_else_and_every_issued_document_stays_tied_to_its_revision()
    {
        var w = await SetupAsync();
        var (id, cartons) = await PackedAsync(w);
        var shp = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}")).GetProperty("shipment").Str("number")!;

        // no template yet: the built-in layout is used and recorded
        var builtIn = await PdfAsync(w.Lead, $"/api/shipping/shipments/{id}/packing-list");
        Assert.Contains($"Packing list {shp}", builtIn.Text);
        foreach (var c in cartons) Assert.Contains(c, builtIn.Text);
        Assert.Contains("Page 1 of 1", builtIn.Text);
        var docs = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/documents")).EnumerateArray().ToList();
        Assert.Single(docs); Assert.True(docs[0].GetProperty("builtIn").GetBoolean()); Assert.Equal(builtIn.DocId, docs[0].Id());

        // viewers cannot create templates
        await Expect(HttpStatusCode.Forbidden, await w.Viewer.PostAsync("/api/shipping/document-templates", new { code = "PLV" + w.Tag, name = "x", kind = 1, description = (string?)null, siteId = w.Site }));
        var (tid, r1) = await TemplateAsync(w.Lead, w.Site, "PL" + w.Tag, 1);
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync("/api/shipping/document-templates", new { code = "pl" + w.Tag, name = "dup", kind = 1, description = (string?)null, siteId = w.Site }), "already exists");
        var detail = await w.Lead.GetElAsync($"/api/shipping/document-templates/{tid}");
        Assert.Equal("Draft", detail.GetProperty("revisions")[0].Str("state"));
        Assert.Equal("Packing list {shipmentNumber}", detail.GetProperty("revisions")[0].GetProperty("layout").GetProperty("header").Str("title"));

        // layout validation
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r1}", Rev(Layout("Bad {nope}", null, Sec("products")))), "unknown placeholder {nope}");
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r1}", Rev(Layout("PL", null, Sec("manifestShipments")))), "not available on a packing list");
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r1}", Rev(Layout("PL", null, Sec("products", columns: ["part", "colour"])))), "unknown column 'colour'");
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r1}", Rev(Layout("PL", null, Sec("barcode")))), "choose the value to encode");
        await Expect(HttpStatusCode.Forbidden, await w.Viewer.PutAsync($"/api/shipping/document-template-revisions/{r1}", Rev(Layout("PL", null, Sec("products")))));

        var layout = Layout("ACME packing list {shipmentNumber}", "Questions: shipping desk, PO {customerPo}",
            Sec("addresses"), Sec("customer"), Sec("products", columns: ["line", "part", "quantity", "uom"]), Sec("cartons"), Sec("lots"), Sec("dimensions"), Sec("weights"),
            Sec("barcode", field: "shipmentNumber"), Sec("qr", title: "Scan to receive", text: "QG:SHP:{shipmentNumber}"), Sec("text", text: "Customer PO {customerPo}"), Sec("signatures", signatures: ["Packed by", "Checked by"]));
        await Expect(HttpStatusCode.NoContent, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r1}", Rev(layout, customerId: w.Customer)));
        await Expect(HttpStatusCode.BadRequest, await UploadLogoAsync(w.Lead, r1, Encoding.ASCII.GetBytes("\x89PNG\r\n\x1a\nnot a jpeg"), "logo.png"), "JPEG");
        await Expect(HttpStatusCode.NoContent, await UploadLogoAsync(w.Lead, r1, Logo, "logo.jpg"));
        Assert.Equal(Logo, await (await w.Viewer.GetAsync($"/api/shipping/document-template-revisions/{r1}/logo")).Content.ReadAsByteArrayAsync());

        // preview with sample data and with the real shipment, marked as a preview
        var sample = await PdfAsync(w.Viewer, $"/api/shipping/document-template-revisions/{r1}/preview");
        Assert.Contains("ACME packing list SHP-SAMPLE", sample.Text); Assert.Contains("PREVIEW - PL" + w.Tag + " revision 1 \\(Draft\\)", sample.Text); Assert.Contains("/Subtype /Image", sample.Text);
        var real = await PdfAsync(w.Lead, $"/api/shipping/document-template-revisions/{r1}/preview?shipmentId={id}");
        Assert.Contains($"ACME packing list {shp}", real.Text); Assert.Contains("Customer PO PO-" + w.Tag, real.Text); Assert.Contains("40 cm", real.Text);
        Assert.Single((await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/documents")).EnumerateArray()); // previews are not issued documents

        // submit; the preparer cannot approve; notes are required
        await Expect(HttpStatusCode.NoContent, await w.Lead.PostAsync($"/api/shipping/document-template-revisions/{r1}/submit"));
        await Expect(HttpStatusCode.Conflict, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r1}", Rev(layout)), "only a draft can be changed");
        await Expect(HttpStatusCode.Forbidden, await w.Lead.PostAsync($"/api/shipping/document-template-revisions/{r1}/approve", new { notes = "Looks right to me.", effectiveFrom = (DateTimeOffset?)null }));
        await Expect(HttpStatusCode.BadRequest, await w.Approver.PostAsync($"/api/shipping/document-template-revisions/{r1}/approve", new { notes = "ok", effectiveFrom = (DateTimeOffset?)null }), "at least 5");
        await Expect(HttpStatusCode.NoContent, await w.Approver.PostAsync($"/api/shipping/document-template-revisions/{r1}/approve", new { notes = "Matches the customer routing guide rev 4.", effectiveFrom = (DateTimeOffset?)null }));
        detail = await w.Lead.GetElAsync($"/api/shipping/document-templates/{tid}");
        Assert.Equal("Effective", detail.GetProperty("revisions")[0].Str("state"));
        Assert.Equal(64, detail.GetProperty("revisions")[0].Str("contentHash")!.Length);

        var v1 = await PdfAsync(w.Lead, $"/api/shipping/shipments/{id}/packing-list");
        Assert.Contains($"ACME packing list {shp}", v1.Text); Assert.Contains("Questions: shipping desk, PO PO-" + w.Tag, v1.Text); Assert.Contains("Packed by signature", v1.Text);
        Assert.Contains("DL-" + w.Tag, v1.Text); Assert.DoesNotContain("PREVIEW", v1.Text);
        var choice = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/document-templates")).EnumerateArray().First(c => c.Int("kind") == 1);
        Assert.Equal("PL" + w.Tag, choice.Str("templateCode")); Assert.Equal(1, choice.Int("revision")); Assert.Contains("customer", choice.Str("reason"));

        // a new revision approved for a future date does not apply yet; the earlier one keeps issuing
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/document-template-revisions/{r1}/submit"));
        var r2 = (await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/document-templates/{tid}/revisions"))).Id();
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/document-templates/{tid}/revisions"), "still a draft");
        var layout2 = Layout("ACME PL v2 {shipmentNumber}", null, Sec("products"), Sec("serials"), Sec("pallets"));
        await Expect(HttpStatusCode.NoContent, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r2}", Rev(layout2, customerId: w.Customer, summary: null)));
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PostAsync($"/api/shipping/document-template-revisions/{r2}/submit"), "Describe the change");
        await Expect(HttpStatusCode.NoContent, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r2}", Rev(layout2, customerId: w.Customer, summary: "Shorter layout")));
        await Expect(HttpStatusCode.NoContent, await w.Lead.PostAsync($"/api/shipping/document-template-revisions/{r2}/submit"));
        await Expect(HttpStatusCode.NoContent, await w.Approver.PostAsync($"/api/shipping/document-template-revisions/{r2}/approve", new { notes = "Effective next week.", effectiveFrom = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(7) }));
        detail = await w.Lead.GetElAsync($"/api/shipping/document-templates/{tid}");
        Assert.Equal("Scheduled", detail.GetProperty("revisions")[0].Str("state")); Assert.Equal("Effective", detail.GetProperty("revisions")[1].Str("state"));
        Assert.Contains("ACME packing list", (await PdfAsync(w.Lead, $"/api/shipping/shipments/{id}/packing-list")).Text);

        // time passes (moved in the database): revision 2 takes over, revision 1 is superseded, earlier documents still reproduce exactly
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.ShippingDocumentTemplateRevisions.Where(x => x.Id == r2).ExecuteUpdateAsync(u => u.SetProperty(x => x.EffectiveFrom, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        var v2 = await PdfAsync(w.Lead, $"/api/shipping/shipments/{id}/packing-list");
        Assert.Contains($"ACME PL v2 {shp}", v2.Text);
        detail = await w.Lead.GetElAsync($"/api/shipping/document-templates/{tid}");
        Assert.Equal("Effective", detail.GetProperty("revisions")[0].Str("state")); Assert.Equal("Superseded", detail.GetProperty("revisions")[1].Str("state"));
        docs = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/documents")).EnumerateArray().ToList();
        Assert.Equal([2, 1, 1], docs.Take(3).Select(d => d.Int("templateRevision")).ToArray());
        foreach (var d in docs)
        {
            var rep = await Expect(HttpStatusCode.OK, await w.Viewer.PostAsync($"/api/shipping/generated-documents/{d.Id()}/reproduce"));
            Assert.True(rep.GetProperty("identical").GetBoolean(), $"document {d.Id()} did not reproduce");
        }
        Assert.Equal(v1.Bytes, (await PdfAsync(w.Viewer, $"/api/shipping/generated-documents/{v1.DocId}")).Bytes);

        // an approved revision cannot be changed behind the approval
        await Expect(HttpStatusCode.Conflict, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{r2}", Rev(layout2, summary: "x")));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rev = await db.ShippingDocumentTemplateRevisions.FirstAsync(x => x.Id == r2);
            var original = rev.LayoutJson;
            rev.LayoutJson = original.Replace("ACME PL v2", "Tampered"); await db.SaveChangesAsync();
            await Expect(HttpStatusCode.Conflict, await w.Lead.GetAsync($"/api/shipping/shipments/{id}/packing-list"), "no longer matches what was approved");
            rev.LayoutJson = original; await db.SaveChangesAsync();
        }
        Assert.Contains($"ACME PL v2 {shp}", (await PdfAsync(w.Lead, $"/api/shipping/shipments/{id}/packing-list")).Text);

        // audit trail records the template changes
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditEntries.AnyAsync(a => a.EntityId == r1 && a.EntityType == nameof(ShippingDocumentTemplateRevision)));
        }
    }

    [Fact]
    public async Task Selection_prefers_customer_then_carrier_then_site_then_priority_refuses_ambiguous_rules_and_issues_bills_of_lading_and_manifests()
    {
        var w = await SetupAsync();
        var (id, _) = await PackedAsync(w);
        var otherCust = (await (await w.Lead.PostOkAsync("/api/master/customers", new { code = "O" + w.Tag, name = "Other " + w.Tag, siteId = w.Site, status = 0 })).JsonAsync()).Id();
        var draft = (await (await w.Lead.PostOkAsync("/api/shipping/shipments", new { kind = 0, customerId = (Guid?)otherCust, supplierId = (Guid?)null, toSiteId = (Guid?)null, shipTo = "Other dock", customerPo = (string?)null,
            promisedOn = (DateOnly?)Today, stagingLocationId = w.Loc["SHIP"], carrierId = (Guid?)null, serviceLevel = (string?)null, notes = (string?)null, siteId = w.Site,
            lines = new[] { new { partId = await PartAsync(w, "DD-"), revision = (string?)null, quantity = 1m, lotId = (Guid?)null } } })).JsonAsync()).Id();
        string? Pl(JsonElement choices) => choices.EnumerateArray().First(c => c.Int("kind") == 1).Str("templateCode");

        var (_, sRev) = await TemplateAsync(w.Lead, w.Site, "SITE" + w.Tag, 1);
        await ApprovedAsync(w.Lead, w.Approver, sRev, Rev(Layout("Site PL {shipmentNumber}", null, Sec("products"))));
        var (cTid, cRev) = await TemplateAsync(w.Lead, w.Site, "CUST" + w.Tag, 1);
        await ApprovedAsync(w.Lead, w.Approver, cRev, Rev(Layout("Customer PL {shipmentNumber}", null, Sec("products")), customerId: w.Customer));
        Assert.Equal("CUST" + w.Tag, Pl(await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/document-templates")));
        Assert.Equal("SITE" + w.Tag, Pl(await w.Lead.GetElAsync($"/api/shipping/shipments/{draft}/document-templates")));

        // same site, customer, carrier and priority as an active template is ambiguous
        var (_, s2Rev) = await TemplateAsync(w.Lead, w.Site, "SITE2" + w.Tag, 1);
        await Expect(HttpStatusCode.NoContent, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{s2Rev}", Rev(Layout("Site 2 {shipmentNumber}", null, Sec("products")))));
        await Expect(HttpStatusCode.Conflict, await w.Lead.PostAsync($"/api/shipping/document-template-revisions/{s2Rev}/submit"), "already applies");
        await ApprovedAsync(w.Lead, w.Approver, s2Rev, Rev(Layout("Site 2 {shipmentNumber}", null, Sec("products")), priority: 50));
        Assert.Equal("SITE2" + w.Tag, Pl(await w.Lead.GetElAsync($"/api/shipping/shipments/{draft}/document-templates")));

        // rule validation
        var otherSite = await app.CreateSiteAsync();
        var (_, admin) = (Guid.Empty, await app.AdminAsync());
        var foreign = (await (await admin.PostOkAsync("/api/master/customers", new { code = "F" + w.Tag, name = "Foreign " + w.Tag, siteId = otherSite, status = 0 })).JsonAsync()).Id();
        var (_, xRev) = await TemplateAsync(w.Lead, w.Site, "X" + w.Tag, 1);
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{xRev}", Rev(Layout("X", null, Sec("products")), customerId: foreign)), "another site");
        var (_, mxRev) = await TemplateAsync(w.Lead, w.Site, "MX" + w.Tag, 2);
        await Expect(HttpStatusCode.BadRequest, await w.Lead.PutAsync($"/api/shipping/document-template-revisions/{mxRev}", Rev(Layout("M", null, Sec("manifestShipments")), customerId: w.Customer)), "many customers");

        // deactivating the customer template falls back to the best site template
        await Expect(HttpStatusCode.NoContent, await w.Lead.PutAsync($"/api/shipping/document-templates/{cTid}", new { name = "Customer", description = (string?)null, active = false }));
        Assert.Equal("SITE2" + w.Tag, Pl(await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/document-templates")));

        // an organization-wide carrier-specific bill of lading needs all-site rights to prepare and approve
        await Expect(HttpStatusCode.Forbidden, await w.Lead.PostAsync("/api/shipping/document-templates", new { code = "ORG" + w.Tag, name = "Org BOL", kind = 0, description = (string?)null, siteId = (Guid?)null }), "every site");
        var (_, orgApprover) = await app.CreateUserAsync([Permissions.ShippingView, Permissions.ShippingApprove]);
        var (_, bRev) = await TemplateAsync(admin, null, "ORG" + w.Tag, 0);
        await ApprovedAsync(admin, orgApprover, bRev, Rev(Layout("Freight BOL {bolNumber}", "Carrier copy",
            Sec("addresses"), Sec("carrier"), Sec("shipment"), Sec("freightTerms", text: "Prepaid"), Sec("products"), Sec("weights"), Sec("instructions"), Sec("barcode", field: "bolNumber"), Sec("signatures", signatures: ["Shipper", "Driver"])), carrierId: w.Carrier));

        // shipping terms and the bill of lading
        await Expect(HttpStatusCode.Conflict, await w.Lead.GetAsync($"/api/shipping/shipments/{id}/bill-of-lading"), "issued when the shipment ships");
        await Expect(HttpStatusCode.NoContent, await w.Lead.PutAsync($"/api/shipping/shipments/{id}/shipping-terms", new { freightTerms = "Collect", specialInstructions = "Call before delivery." }));
        await Expect(HttpStatusCode.Forbidden, await w.Viewer.PutAsync($"/api/shipping/shipments/{id}/shipping-terms", new { freightTerms = "Prepaid", specialInstructions = (string?)null }));
        var cartons = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}")).GetProperty("containers").EnumerateArray().Select(c => c.GetProperty("container").Str("number")).ToList();
        await Expect(HttpStatusCode.OK, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/verify", new { scans = cartons.Select(c => new { code = "QG:CTN:" + c, quantity = (decimal?)null }) }));
        await Expect(HttpStatusCode.NoContent, await w.Lead.PostAsync($"/api/shipping/shipments/{id}/ship", new { carrierId = w.Carrier, trackingNumber = "PRO-" + w.Tag, serviceLevel = "LTL standard", grossWeight = (decimal?)6m }));
        await Expect(HttpStatusCode.Conflict, await w.Lead.PutAsync($"/api/shipping/shipments/{id}/shipping-terms", new { freightTerms = "Prepaid", specialInstructions = (string?)null }));
        var bolNo = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}")).GetProperty("shipment").Str("bolNumber")!;
        var bol = await PdfAsync(w.Viewer, $"/api/shipping/shipments/{id}/bill-of-lading");
        Assert.Contains($"Freight BOL {bolNo}", bol.Text); Assert.Contains("Collect", bol.Text); Assert.Contains("Call before delivery.", bol.Text); Assert.Contains("PRO-" + w.Tag, bol.Text);
        Assert.Contains("Driver signature", bol.Text); Assert.Contains("6 kg", bol.Text);
        var choiceBol = (await w.Lead.GetElAsync($"/api/shipping/shipments/{id}/document-templates")).EnumerateArray().First(c => c.Int("kind") == 0);
        Assert.Equal("ORG" + w.Tag, choiceBol.Str("templateCode")); Assert.Contains("carrier", choiceBol.Str("reason"));

        // the day's manifest for the carrier (no manifest template: built-in layout)
        await Expect(HttpStatusCode.Conflict, await w.Lead.GetAsync($"/api/shipping/manifest?siteId={w.Site}&carrierId={w.Carrier}&date={Today.AddDays(-30):yyyy-MM-dd}"), "Nothing was shipped");
        var man = await PdfAsync(w.Viewer, $"/api/shipping/manifest?siteId={w.Site}&carrierId={w.Carrier}&date={Today:yyyy-MM-dd}");
        var site = (await w.Lead.GetElAsync("/api/organization/sites")).EnumerateArray().First(s => s.Id() == w.Site).Str("code");
        Assert.Contains($"Shipping manifest MAN-{site}-DC{w.Tag}-{Today:yyyyMMdd}", man.Text);
        Assert.Contains((await w.Lead.GetElAsync($"/api/shipping/shipments/{id}")).GetProperty("shipment").Str("number")!, man.Text);
        Assert.Contains("Carrier driver signature", man.Text);
        var issued = (await w.Viewer.GetElAsync($"/api/shipping/generated-documents?siteId={w.Site}&kind=2")).EnumerateArray().ToList();
        Assert.Single(issued); Assert.True(issued[0].GetProperty("builtIn").GetBoolean());
        await Expect(HttpStatusCode.Forbidden, await (await app.CreateUserAsync([Permissions.ShippingView], otherSite)).Client.GetAsync($"/api/shipping/manifest?siteId={w.Site}&carrierId={w.Carrier}&date={Today:yyyy-MM-dd}"));
    }
}
