using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Integration;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Integration;
using QualGuardIQ.Infrastructure.Modules.Integration.Erp;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 9: Business Central manufacturing adapter (extension API pages or published web services). Recorded responses only; no tenant is contacted.</summary>
[Collection("api")]
public sealed class GapClosureBcManufacturingTests(TestApp app)
{
    const string Token = """{"token_type":"Bearer","expires_in":3599,"access_token":"bc-token"}""";

    static ErpCallContext Ctx(FixtureHandler h, string edition, JsonObject options, Dictionary<string, string>? map = null)
    {
        var c = new ErpConnection { Name = "bc", System = ErpSystem.BcManufacturing, Edition = edition, BaseUrl = "", Account = "tenant-1", Company = edition == BcManufacturingAdapter.WebServices ? "CRONUS USA, Inc." : "c-1", ClientId = "app-1" };
        ErpHttp.Delay = (_, _) => Task.CompletedTask;
        return new ErpCallContext { Connection = c, Secret = "s", Options = options, Http = new ErpHttp(new HttpClient(h), c.Id, 2, 6000), Tokens = new ErpTokenCache(), FieldMap = map ?? new(), Now = DateTimeOffset.UtcNow };
    }

    [Fact]
    public async Task Extension_api_reads_released_orders_with_configured_publisher_filter_and_delta()
    {
        var h = new FixtureHandler().On(r => r.RequestUri!.Host == "login.microsoftonline.com", () => FixtureHandler.Json(Token))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/productionOrders"), () => FixtureHandler.Json("""{"value":[{"id":"g-1","number":"RPO-101","sourceNumber":"1896-S","quantity":40,"dueDate":"2026-10-20","status":"Released","lastModifiedDateTime":"2026-10-17T10:00:00Z"}],"@odata.nextLink":"https://api.businesscentral.dynamics.com/next?$skiptoken=x"}"""));
        var a = new BcManufacturingAdapter();
        var missing = Ctx(h, BcManufacturingAdapter.CustomApi, []);
        var err = await Assert.ThrowsAsync<ErpException>(() => a.ReadAsync(missing, ErpEntity.ProductionOrder, new(null, null), CancellationToken.None));
        Assert.Contains("apiPublisher", err.Message); // nothing about the extension is assumed

        var c = Ctx(h, BcManufacturingAdapter.CustomApi, new JsonObject { ["apiPublisher"] = "contoso", ["apiGroup"] = "manufacturing", ["apiVersion"] = "v2.0" });
        var page = await a.ReadAsync(c, ErpEntity.ProductionOrder, new(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null), CancellationToken.None);
        var r = Assert.Single(page.Records);
        Assert.Equal("g-1", r.ExternalId); Assert.Equal("RPO-101", r.Fields["number"]!.ToString()); Assert.Equal("1896-S", r.Fields["item"]!.ToString());
        Assert.NotNull(page.NextPageToken);
        var get = h.Requests.First(x => x.Uri.AbsolutePath.EndsWith("/productionOrders")).Uri;
        Assert.StartsWith("https://api.businesscentral.dynamics.com/v2.0/tenant-1/production/api/contoso/manufacturing/v2.0/companies(c-1)/productionOrders?$filter=", get.ToString());
        Assert.Equal("(status eq 'Released') and lastModifiedDateTime gt 2026-10-01T00:00:00Z", Uri.UnescapeDataString(get.Query)["?$filter=".Length..]);
        Assert.Equal("https://api.businesscentral.dynamics.com/.default", h.Requests.First(x => x.Uri.Host == "login.microsoftonline.com").Body!.Split('&').Select(p => p.Split('=')).First(p => p[0] == "scope")[1].Replace("%3A", ":").Replace("%2F", "/"));

        // entities outside the connection's list are refused, entity set and filter are configurable
        await Assert.ThrowsAsync<ErpException>(() => a.ReadAsync(c, ErpEntity.CapacityLedgerEntry, new(null, null), CancellationToken.None));
        var c2 = Ctx(h, BcManufacturingAdapter.CustomApi, new JsonObject { ["apiPublisher"] = "contoso", ["apiGroup"] = "mfg", ["entities"] = new JsonArray("ProductionOrder"), ["entitySet.ProductionOrder"] = "relProdOrders", ["filter.ProductionOrder"] = "", ["modifiedField"] = "" });
        h.On(r => r.RequestUri!.AbsolutePath.EndsWith("/relProdOrders"), () => FixtureHandler.Json("""{"value":[]}"""));
        await a.ReadAsync(c2, ErpEntity.ProductionOrder, new(DateTimeOffset.UtcNow, null), CancellationToken.None);
        Assert.Equal("https://api.businesscentral.dynamics.com/v2.0/tenant-1/production/api/contoso/mfg/v1.0/companies(c-1)/relProdOrders", h.Requests.Last().Uri.ToString());
    }

    [Fact]
    public async Task Web_services_use_composite_keys_and_the_test_reports_unpublished_pages_and_unverified_fields()
    {
        var h = new FixtureHandler().On(r => r.RequestUri!.Host == "login.microsoftonline.com", () => FixtureHandler.Json(Token))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/ReleasedProductionOrders"), () => FixtureHandler.Json("""{"value":[{"No":"101001","Source_No":"1896-S","Quantity":40,"Due_Date":"2026-10-20","Status":"Released"}]}"""))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/ReleasedProdOrderLines"), () => FixtureHandler.Json("""{"value":[{"Prod_Order_No":"101001","Line_No":10000,"Item_No":"1896-S","Quantity":40,"Finished_Quantity":12}]}"""))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/WorkCenters"), () => FixtureHandler.Json("""{"value":[{"No":"100","Name":"Assembly"}]}"""))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/CapacityLedgerEntries"), () => FixtureHandler.Json("""{"error":{"code":"Authorization_InsufficientPermissions","message":"You do not have the following permissions on TableData Capacity Ledger Entry: Read."}}""", HttpStatusCode.Forbidden));
        var a = new BcManufacturingAdapter();
        var opts = new JsonObject { ["entities"] = new JsonArray("ProductionOrder", "ProductionOrderLine", "WorkCenter", "MachineCenter") };
        var c = Ctx(h, BcManufacturingAdapter.WebServices, opts);

        var lines = await a.ReadAsync(c, ErpEntity.ProductionOrderLine, new(null, null), CancellationToken.None);
        Assert.Equal("101001|10000", lines.Records.Single().ExternalId);
        Assert.Equal("12", lines.Records[0].Fields["finishedQuantity"]!.ToString());
        Assert.EndsWith("/v2.0/tenant-1/production/ODataV4/Company('CRONUS USA, Inc.')/ReleasedProdOrderLines", Uri.UnescapeDataString(h.Requests.Last().Uri.AbsolutePath));

        // MachineCenters not published → test fails and says why; lines lack Remaining_Quantity/Scrap_Percent → flagged as unverified
        var e = await Assert.ThrowsAsync<ErpException>(() => a.TestAsync(c, CancellationToken.None));
        Assert.Contains("MachineCenter: FAILED, 'MachineCenters' is not published on the Web Services page", e.Message);
        Assert.Contains("ProductionOrderLine: OK, unverified fields (not in the sample row): remainingQuantity←Remaining_Quantity, scrapPercent←Scrap_Percent", e.Message);
        Assert.Contains("WorkCenter: OK, all mapped fields present.", e.Message);

        // remove the unpublished page, add one the app lacks permission for: the reason is a permission set
        opts["entities"] = new JsonArray("ProductionOrder", "CapacityLedgerEntry");
        e = await Assert.ThrowsAsync<ErpException>(() => a.TestAsync(c, CancellationToken.None));
        Assert.Contains("CapacityLedgerEntry: FAILED, the Entra application lacks a permission set", e.Message);
        opts["entities"] = new JsonArray("ProductionOrder", "WorkCenter");
        var ok = await a.TestAsync(c, CancellationToken.None);
        Assert.StartsWith("Token issued (WebServices). ProductionOrder: OK", ok);
        Assert.Contains("unverified fields (not in the sample row): location←Location_Code", ok);
        Assert.Equal(11, a.Capabilities(BcManufacturingAdapter.WebServices).Read.Count);
    }

    [Fact]
    public async Task Connection_is_disabled_until_tested_then_syncs_orders_lines_machine_centers_and_serials_and_keeps_ledgers_for_review()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([Permissions.IntegrationsView, Permissions.IntegrationsEdit, Permissions.ProductionView, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var part = $"BC-{tag}";
        await eng.PostOkAsync("/api/master/parts", new { partNumber = part, name = "Gear housing", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = site, status = 0 });
        var h = new FixtureHandler().On(r => r.RequestUri!.Host == "login.microsoftonline.com", () => FixtureHandler.Json(Token))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/ReleasedProductionOrders"), () => FixtureHandler.Json($$"""{"value":[{"No":"RPO-{{tag}}","Source_No":"{{part}}","Quantity":50,"Due_Date":"2026-11-02","Status":"Released","Location_Code":"MAIN"}]}"""))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/ReleasedProdOrderLines"), () => FixtureHandler.Json($$"""{"value":[{"Prod_Order_No":"RPO-{{tag}}","Line_No":10000,"Item_No":"{{part}}","Quantity":30,"Finished_Quantity":20,"Remaining_Quantity":10,"Scrap_Percent":0},{"Prod_Order_No":"RPO-{{tag}}","Line_No":20000,"Item_No":"{{part}}","Quantity":20,"Finished_Quantity":5,"Remaining_Quantity":15,"Scrap_Percent":0}]}"""))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/MachineCenters"), () => FixtureHandler.Json($$"""{"value":[{"No":"MC-{{tag}}","Name":"CNC lathe 3","Work_Center_No":"100"}]}"""))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/ItemLedgerEntries") && Uri.UnescapeDataString(r.RequestUri.Query).Contains("Serial_No ne ''"), () => FixtureHandler.Json($$"""{"value":[{"Entry_No":501,"Serial_No":"SN-{{tag}}-1","Item_No":"{{part}}","Lot_No":""}]}"""))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/CapacityLedgerEntries"), () => FixtureHandler.Json($$"""{"value":[{"Entry_No":9001,"Order_No":"RPO-{{tag}}","Operation_No":"10","Work_Center_No":"100","Output_Quantity":20,"Scrap_Quantity":1,"Posting_Date":"2026-10-16"}]}"""));
        app.Services.GetRequiredService<ErpHttpHandlerProvider>().Override = () => h;
        try
        {
            var options = new Dictionary<string, object> { ["entities"] = new[] { "ProductionOrder", "ProductionOrderLine", "MachineCenter", "Serial", "CapacityLedgerEntry" } };
            var id = (await (await eng.PostOkAsync("/api/integrations", new { name = "BC mfg " + tag, system = 9, edition = "WebServices", account = "tenant-1", company = "CRONUS", clientId = "app-1", options, siteId = site, inventorySystemOfRecord = false })).JsonAsync()).Id();
            await eng.PutAsync($"/api/integrations/{id}/secrets", new { secret = "client-secret" });
            var conn = await eng.GetElAsync($"/api/integrations/{id}");
            Assert.False(conn.GetProperty("enabled").GetBoolean());
            Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);
            var v = await eng.GetElAsync($"/api/integrations/{id}/validation");
            Assert.Contains("Recorded Business Central manufacturing responses", v.Str("fixtureTested"));
            Assert.Contains(v.GetProperty("checklist").EnumerateArray(), x => x.Str("step") == "Business Central manufacturing source");
            Assert.Contains(v.GetProperty("assumptions").EnumerateArray(), x => x.Str("topic") == "Published pages" && x.Str("status") == "Unverified");

            var test = await (await eng.PostOkAsync($"/api/integrations/{id}/test")).JsonAsync();
            Assert.True(test.GetProperty("ok").GetBoolean(), test.ToString());
            Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/integrations/{id}/enabled", new { enabled = true })).StatusCode);

            foreach (var entity in new[] { "ProductionOrder", "ProductionOrderLine", "MachineCenter", "Serial", "CapacityLedgerEntry" })
            {
                Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/integrations/{id}/mappings/{entity}", new { direction = 0, mode = 1, conflict = 0, cron = "0 * * * *", fieldMap = (object?)null, validationRules = (object?)null })).StatusCode);
                await eng.PostOkAsync($"/api/integrations/{id}/mappings/{entity}/run");
            }
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var wo = await db.WorkOrders.SingleAsync(x => x.Number == $"RPO-{tag}");
            Assert.Equal(50m, wo.Quantity); Assert.Equal(25m, wo.QuantityCompleted); Assert.NotNull(wo.PartId); // finished = sum of both lines
            Assert.True(await db.WorkCenters.AnyAsync(x => x.Code == $"MC-{tag}" && x.Name == "CNC lathe 3" && x.SiteId == site));
            Assert.True(await db.SerialUnits.AnyAsync(x => x.SerialNumber == $"SN-{tag}-1" && x.PartId == wo.PartId));
            var ledger = await db.ErpChanges.SingleAsync(x => x.ConnectionId == id && x.Entity == ErpEntity.CapacityLedgerEntry);
            Assert.NotEqual(ChangeQueueStatus.Applied, ledger.Status);
            Assert.Contains("review only", ledger.ValidationErrors ?? "");
        }
        finally { app.Services.GetRequiredService<ErpHttpHandlerProvider>().Override = null; }
    }
}
