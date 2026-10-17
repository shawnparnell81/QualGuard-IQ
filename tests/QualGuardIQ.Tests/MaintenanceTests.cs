using System.Net;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class MaintenanceTests(TestApp app)
{
    [Fact]
    public async Task Tooling_preventive_maintenance_breakdown_and_repair_history()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (_, metro) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.CalibrationEdit], site);
        var (_, tech) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.MaintenanceEdit], site);
        var (_, viewer) = await app.CreateUserAsync([Permissions.CalibrationView], site);

        var mold = (await (await metro.PostOkAsync("/api/calibration/equipment", new { name = "Injection mold 4-cavity", manufacturer = "Hasco", siteId = site, calibrationIntervalDays = 365, category = "Mold", assetNumber = "A-1001", requiresCalibration = false })).JsonAsync()).Id();
        var d = await metro.GetElAsync($"/api/calibration/equipment/{mold}");
        Assert.Equal("Mold", d.Str("category"));
        Assert.Equal(System.Text.Json.JsonValueKind.Null, d.GetProperty("nextDueOn").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await metro.PostAsync($"/api/calibration/equipment/{mold}/events", new { performedOn = today, performedBy = "Lab", result = 0 })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await metro.PostAsync($"/api/calibration/equipment/{mold}/maintenance-plans", new { title = "Clean vents", intervalDays = 30 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/calibration/equipment/{mold}/maintenance-plans", new { title = "Clean vents", intervalDays = 30 })).StatusCode);
        var plan = (await (await tech.PostOkAsync($"/api/calibration/equipment/{mold}/maintenance-plans", new { title = "Clean vents and check ejector pins", intervalDays = 30, firstDueOn = today })).JsonAsync()).Id();
        var list = await metro.GetElAsync("/api/calibration/equipment?maintenanceDueWithinDays=0&category=Mold");
        Assert.Contains(list.EnumerateArray(), x => x.Id() == mold);

        Assert.Equal(HttpStatusCode.BadRequest, (await tech.PostAsync($"/api/calibration/equipment/{mold}/maintenance", new { kind = 0, performedOn = today, performedBy = "J. Tech", description = "Cleaned" })).StatusCode); // no plan
        await tech.PostOkAsync($"/api/calibration/equipment/{mold}/maintenance", new { kind = 0, planId = plan, performedOn = today, performedBy = "J. Tech", description = "Cleaned vents; pins OK", downtimeHours = 1.5m });
        d = await metro.GetElAsync($"/api/calibration/equipment/{mold}");
        Assert.Equal(today.AddDays(30).ToString("yyyy-MM-dd"), d.GetProperty("plans")[0].Str("nextDueOn"));

        await tech.PostOkAsync($"/api/calibration/equipment/{mold}/maintenance", new { kind = 2, performedOn = today, performedBy = "Operator", description = "Hot runner zone 3 failed", downtimeHours = 6m });
        d = await metro.GetElAsync($"/api/calibration/equipment/{mold}");
        Assert.Equal(4, d.Int("status")); // Down
        Assert.DoesNotContain("repair", d.GetProperty("allowed").EnumerateArray().Select(x => x.GetString())); // metro lacks maintenance.edit
        Assert.Contains("repair", (await tech.GetElAsync($"/api/calibration/equipment/{mold}")).GetProperty("allowed").EnumerateArray().Select(x => x.GetString()));
        await tech.PostOkAsync($"/api/calibration/equipment/{mold}/maintenance", new { kind = 1, performedOn = today, performedBy = "J. Tech", description = "Replaced heater band", downtimeHours = 3m, returnToService = true });
        d = await metro.GetElAsync($"/api/calibration/equipment/{mold}");
        Assert.Equal(0, d.Int("status"));
        Assert.Equal(3, d.GetProperty("maintenance").GetArrayLength());
        Assert.Equal(10.5m, d.GetProperty("downtime").GetDecimal());
        // records are immutable: there is no edit or delete endpoint for maintenance events
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await tech.PutAsync($"/api/calibration/equipment/{mold}/maintenance", new { })).StatusCode);
    }

    [Fact]
    public async Task Repair_affecting_measurement_requires_a_calibration_after_the_repair()
    {
        var today = TestDates.Today;
        var site = await app.CreateSiteAsync();
        var (_, metro) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.CalibrationEdit, Permissions.MaintenanceEdit], site);
        var (_, mgr) = await app.CreateUserAsync([Permissions.CalibrationView, Permissions.CalibrationApprove], site);
        var cmm = (await (await metro.PostOkAsync("/api/calibration/equipment", new { name = "CMM", siteId = site, calibrationIntervalDays = 365, nextDueOn = today.AddDays(100), category = "Inspection equipment" })).JsonAsync()).Id();
        await metro.PostOkAsync($"/api/calibration/equipment/{cmm}/events", new { performedOn = today.AddDays(-10), performedBy = "Lab", result = 0 });
        await metro.PostOkAsync($"/api/calibration/equipment/{cmm}/maintenance", new { kind = 2, performedOn = today.AddDays(-2), performedBy = "Operator", description = "Probe crash" });
        await metro.PostOkAsync($"/api/calibration/equipment/{cmm}/maintenance", new { kind = 1, performedOn = today.AddDays(-1), performedBy = "OEM", description = "Replaced probe head", returnToService = true, calibrationRequired = true });
        var d = await metro.GetElAsync($"/api/calibration/equipment/{cmm}");
        Assert.Equal(1, d.Int("status")); // quarantined pending calibration
        Assert.Equal(HttpStatusCode.BadRequest, (await mgr.PostAsync($"/api/calibration/equipment/{cmm}/return-to-service")).StatusCode); // last calibration predates repair
        await metro.PostOkAsync($"/api/calibration/equipment/{cmm}/events", new { performedOn = today, performedBy = "Lab", result = 0 });
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/calibration/equipment/{cmm}/return-to-service")).StatusCode);
        Assert.Equal(0, (await metro.GetElAsync($"/api/calibration/equipment/{cmm}")).Int("status"));

        var rv = (await metro.GetElAsync($"/api/calibration/equipment/{cmm}")).GetProperty("rowVersion").GetUInt32();
        var owner = (await metro.GetElAsync($"/api/calibration/equipment/{cmm}")).Str("ownerId");
        Assert.Equal(HttpStatusCode.NoContent, (await metro.PutAsync($"/api/calibration/equipment/{cmm}", new { name = "CMM Zeiss Contura", location = "Metrology lab", ownerId = owner, calibrationIntervalDays = 180, category = "Inspection equipment", assetNumber = "A-77", rowVersion = rv })).StatusCode);
        d = await metro.GetElAsync($"/api/calibration/equipment/{cmm}");
        Assert.Equal(today.AddDays(180).ToString("yyyy-MM-dd"), d.Str("nextDueOn"));
        Assert.Equal(HttpStatusCode.Forbidden, (await metro.PostAsync($"/api/calibration/equipment/{cmm}/retire")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await mgr.PostAsync($"/api/calibration/equipment/{cmm}/retire")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await metro.PostAsync($"/api/calibration/equipment/{cmm}/maintenance", new { kind = 1, performedOn = today, performedBy = "x", description = "x" })).StatusCode);
    }
}
