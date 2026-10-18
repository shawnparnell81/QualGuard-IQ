using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.QualityOps;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class QualityOpsTests(TestApp app)
{
    private static readonly string[] Ops = [Permissions.QualityOpsView, Permissions.QualityOpsEdit];

    private static async Task<Guid> PartAsync(ApiClient c, Guid site) =>
        (await (await c.PostOkAsync("/api/master/parts", new { partNumber = "P" + Guid.NewGuid().ToString("N")[..8], name = "Housing", partType = 0, revision = "A", unitOfMeasure = "EA", siteId = site, status = 0 })).JsonAsync()).Id();

    private static object Event(Guid site, string title, Guid? partId = null, string? lot = null, string? reference = null, int source = 0) =>
        new { siteId = site, title, description = "Seen at final inspection", category = "Product", partId, lotNumber = lot, source, clientReference = reference };

    [Fact]
    public async Task Quality_event_is_deduplicated_rated_from_the_matrix_and_escalated_to_a_deviation()
    {
        var site = await app.CreateSiteAsync();
        var (_, reporter) = await app.CreateUserAsync([Permissions.QualityEventsReport], site);
        var (_, qe) = await app.CreateUserAsync([.. Ops, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var (_, outsider) = await app.CreateUserAsync(Ops, await app.CreateSiteAsync());
        var reference = "MES-" + Guid.NewGuid().ToString("N")[..8];

        Assert.Equal(HttpStatusCode.BadRequest, (await reporter.PostAsync("/api/quality-ops/events", new { siteId = site, title = "", description = "x" })).StatusCode);
        var first = await (await reporter.PostOkAsync("/api/quality-ops/events", Event(site, "Porosity on housing bore", reference: reference, source: 2))).JsonAsync();
        Assert.StartsWith("QE-", first.Str("number"));
        Assert.False(first.GetProperty("duplicate").GetBoolean());
        var again = await (await reporter.PostOkAsync("/api/quality-ops/events", Event(site, "Porosity on housing bore", reference: reference, source: 2))).JsonAsync();
        Assert.True(again.GetProperty("duplicate").GetBoolean());
        Assert.Equal(first.Id(), again.Id());
        Assert.Equal(HttpStatusCode.Forbidden, (await reporter.GetAsync("/api/quality-ops/events")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/quality-ops/events/{first.Id()}")).StatusCode);

        var id = first.Id();
        await qe.PostOkAsync($"/api/quality-ops/events/{id}/triage");
        Assert.Equal(HttpStatusCode.BadRequest, (await qe.PostAsync($"/api/quality-ops/events/{id}/classify", new { classification = "Nonconformance", likelihood = 9, impact = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await qe.PostAsync($"/api/quality-ops/events/{id}/classify", new { classification = "Not a class", likelihood = 4, impact = 4 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qe.PostAsync($"/api/quality-ops/events/{id}/classify", new { classification = "Nonconformance", likelihood = 4, impact = 4, notes = "Several bores" })).StatusCode);
        var d = (await qe.GetElAsync($"/api/quality-ops/events/{id}")).GetProperty("event");
        Assert.Equal("Critical", d.Str("severity"));
        var respond = d.GetProperty("respondBy").GetDateTimeOffset() - d.GetProperty("occurredAt").GetDateTimeOffset();
        Assert.Equal(TimeSpan.FromHours(4), respond);

        var esc = await (await qe.PostOkAsync($"/api/quality-ops/events/{id}/escalate", new { target = "deviation", title = "Use housings with bore porosity under 0.2 mm" })).JsonAsync();
        Assert.Equal("Deviation", esc.Str("type"));
        Assert.StartsWith("DEV-", esc.Str("number"));
        d = (await qe.GetElAsync($"/api/quality-ops/events/{id}")).GetProperty("event");
        Assert.Equal(esc.Str("number"), d.Str("escalatedToNumber"));
        Assert.Equal(HttpStatusCode.Conflict, (await qe.PostAsync($"/api/quality-ops/events/{id}/escalate", new { target = "investigation" })).StatusCode);
        var list = await qe.GetElAsync($"/api/quality-ops/events?q={first.Str("number")}");
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Complaint_automation_event_converts_to_a_customer_complaint()
    {
        var site = await app.CreateSiteAsync();
        var (_, cs) = await app.CreateUserAsync([.. Ops, Permissions.MasterDataView, Permissions.MasterDataEdit, Permissions.ComplaintsView, Permissions.ComplaintsEdit], site);
        var (_, noComplaints) = await app.CreateUserAsync(Ops, site);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var customer = (await (await cs.PostOkAsync("/api/master/customers", new { code = "C" + tag, name = "Acme " + tag, siteId = site, status = 0 })).JsonAsync()).Id();
        var noCustomer = (await (await cs.PostOkAsync("/api/quality-ops/events", Event(site, "Rattle reported", source: 3))).JsonAsync()).Id();
        var ev = (await (await cs.PostOkAsync("/api/quality-ops/events", new { siteId = site, title = "Customer reports cracked bezel", description = "Three units cracked on arrival", category = "Customer",
            customerId = customer, lotNumber = "CB-9", source = 3, clientReference = "CRM-" + tag })).JsonAsync()).Id();
        foreach (var id in new[] { noCustomer, ev })
        {
            await cs.PostOkAsync($"/api/quality-ops/events/{id}/triage");
            Assert.Equal(HttpStatusCode.NoContent, (await cs.PostAsync($"/api/quality-ops/events/{id}/classify", new { classification = "Complaint", likelihood = 3, impact = 4 })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await cs.PostAsync($"/api/quality-ops/events/{noCustomer}/escalate", new { target = "complaint" })).StatusCode); // customer missing
        Assert.Equal(HttpStatusCode.Forbidden, (await noComplaints.PostAsync($"/api/quality-ops/events/{ev}/escalate", new { target = "complaint" })).StatusCode);
        var esc = await (await cs.PostOkAsync($"/api/quality-ops/events/{ev}/escalate", new { target = "complaint" })).JsonAsync();
        Assert.Equal("Complaint", esc.Str("type"));
        var complaint = await cs.GetElAsync($"/api/complaints/{esc.Id()}");
        var c = complaint.TryGetProperty("complaint", out var inner) ? inner : complaint;
        Assert.Equal("Customer reports cracked bezel", c.Str("title"));
        Assert.Equal("CB-9", c.Str("lotNumber"));
        Assert.Equal("CRM-" + tag, c.Str("customerReference"));
        Assert.Equal(customer, c.GetProperty("customerId").GetGuid());
        Assert.Equal(esc.Str("number"), (await cs.GetElAsync($"/api/quality-ops/events/{ev}")).GetProperty("event").Str("escalatedToNumber"));
    }

    [Fact]
    public async Task Deviation_is_completed_approved_through_a_delegate_and_used_within_its_limit()
    {
        var site = await app.CreateSiteAsync();
        var (_, eng) = await app.CreateUserAsync([.. Ops, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var (approverId, approver) = await app.CreateUserAsync([.. Ops, Permissions.QualityOpsApprove], site);
        var (deputyId, deputy) = await app.CreateUserAsync([Permissions.QualityOpsView], site);
        var part = await PartAsync(eng, site);
        var today = TestDates.Today;
        var input = new
        {
            siteId = site, kind = 0, title = "Alternate gasket material", requirement = "Drawing note 4: EPDM gasket", deviationDescription = "Use FKM gasket for 500 units",
            justification = "EPDM supply interrupted", riskAssessment = (string?)null, partId = part, quantityLimit = 500m, effectiveFrom = today, effectiveTo = today.AddDays(30), customerApprovalRequired = false
        };
        var dev = await (await eng.PostOkAsync("/api/quality-ops/deviations", input)).JsonAsync();
        var id = dev.Id();
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/quality-ops/deviations/{id}/submit")).StatusCode); // risk assessment missing
        var row = (await eng.GetElAsync($"/api/quality-ops/deviations/{id}")).GetProperty("deviation");
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PutAsync($"/api/quality-ops/deviations/{id}", new
        {
            input.siteId, input.kind, input.title, input.requirement, input.deviationDescription, input.justification, riskAssessment = "Chemical compatibility confirmed; low risk",
            input.partId, input.quantityLimit, input.effectiveFrom, input.effectiveTo, input.customerApprovalRequired, rowVersion = row.GetProperty("rowVersion").GetUInt32()
        })).StatusCode);

        // the approver delegates approvals of deviations to a deputy who does not hold the approve permission
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.PostAsync("/api/quality-ops/delegations", new { delegateId = approverId, startsAt = DateTimeOffset.UtcNow, endsAt = DateTimeOffset.UtcNow.AddDays(7), reason = "Leave" })).StatusCode);
        await approver.PostOkAsync("/api/quality-ops/delegations", new { delegateId = deputyId, startsAt = DateTimeOffset.UtcNow.AddMinutes(-1), endsAt = DateTimeOffset.UtcNow.AddDays(7), recordType = "Deviation", reason = "Annual leave" });
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/quality-ops/deviations/{id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await eng.PostAsync($"/api/quality-ops/deviations/{id}/use", new { quantity = 10 })).StatusCode); // not approved yet

        var approval = await deputy.PendingApprovalIdAsync(dev.Str("number"));
        Assert.True((await deputy.SignAsync(approval)).IsSuccessStatusCode);
        var detail = await eng.GetElAsync($"/api/quality-ops/deviations/{id}");
        Assert.Equal((int)QualityDeviationStatus.Approved, detail.GetProperty("deviation").Int("status"));
        Assert.True(detail.GetProperty("inForce").GetBoolean());
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var decision = await db.ApprovalDecisions.AsNoTracking().FirstAsync(x => x.SignerId == deputyId);
            Assert.Equal(approverId, decision.OnBehalfOfId);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/quality-ops/deviations/{id}/use", new { quantity = 300, lot = "L-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await eng.PostAsync($"/api/quality-ops/deviations/{id}/use", new { quantity = 201, lot = "L-2" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await eng.PostAsync($"/api/quality-ops/deviations/{id}/use", new { quantity = 200, lot = "L-2" })).StatusCode);
        detail = await eng.GetElAsync($"/api/quality-ops/deviations/{id}");
        Assert.False(detail.GetProperty("inForce").GetBoolean()); // quantity used up
    }

    [Fact]
    public async Task Release_is_blocked_by_open_quality_events_and_signed_when_clear()
    {
        var site = await app.CreateSiteAsync();
        var (_, qe) = await app.CreateUserAsync([.. Ops, Permissions.MasterDataView, Permissions.MasterDataEdit], site);
        var (_, qa) = await app.CreateUserAsync([.. Ops, Permissions.QualityOpsApprove], site);
        var part = await PartAsync(qe, site);
        var ev = (await (await qe.PostOkAsync("/api/quality-ops/events", Event(site, "Label smudged", part, "LOT-77"))).JsonAsync()).Id();
        var rel = (await (await qe.PostOkAsync("/api/quality-ops/releases", new { siteId = site, kind = 1, partId = part, lotNumber = "LOT-77", quantity = 1200m })).JsonAsync()).Id();
        var d = await qa.GetElAsync($"/api/quality-ops/releases/{rel}");
        Assert.False(d.GetProperty("actions").GetProperty("release").GetBoolean());
        Assert.Contains(d.GetProperty("checks").EnumerateArray(), c => c.Str("key") == "events" && !c.GetProperty("passed").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await qa.PostAsync($"/api/quality-ops/releases/{rel}/decision", new { release = true, password = "long-enough-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await qe.PostAsync($"/api/quality-ops/releases/{rel}/decision", new { release = true, password = "long-enough-password" })).StatusCode);

        await qe.PostOkAsync($"/api/quality-ops/events/{ev}/triage");
        Assert.Equal(HttpStatusCode.NoContent, (await qe.PostAsync($"/api/quality-ops/events/{ev}/close", new { notes = "Cosmetic; relabelled" })).StatusCode);
        Assert.False((await qa.PostAsync($"/api/quality-ops/releases/{rel}/decision", new { release = true, password = "wrong-password-here" })).IsSuccessStatusCode);
        var ok = await (await qa.PostOkAsync($"/api/quality-ops/releases/{rel}/decision", new { release = true, password = "long-enough-password", comment = "All checks clear" })).JsonAsync();
        Assert.Equal("Batch released", ok.Str("decisionMeaning"));
        d = await qa.GetElAsync($"/api/quality-ops/releases/{rel}");
        Assert.True(d.GetProperty("signatureValid").GetBoolean());
        var board = await qa.GetElAsync("/api/quality-ops/releases/board");
        Assert.True(board.TryGetProperty("releases", out _));
    }

    [Fact]
    public async Task Register_entries_validate_fields_and_follow_their_status_transitions()
    {
        var site = await app.CreateSiteAsync();
        var (_, sqe) = await app.CreateUserAsync([.. Ops, Permissions.SuppliersView, Permissions.SuppliersEdit], site);
        var (_, qa) = await app.CreateUserAsync([.. Ops, Permissions.QualityOpsApprove], site);
        var registers = await sqe.GetElAsync("/api/quality-ops/registers");
        Assert.DoesNotContain(registers.EnumerateArray(), r => r.Str("key") == "laboratory-samples"); // off until switched on
        Assert.Equal(6, registers.GetArrayLength());
        var supplier = (await (await sqe.PostOkAsync("/api/suppliers", new { code = "S" + Guid.NewGuid().ToString("N")[..8], legalName = "Die Castings Co", category = "Castings", criticality = 1, risk = 1, siteId = site })).JsonAsync()).Id();
        const string url = "/api/quality-ops/registers/supplier-change-notifications";
        Assert.Equal(HttpStatusCode.BadRequest, (await sqe.PostAsync(url, new { siteId = site, title = "Die change", supplierId = supplier, details = new { changeType = "Process" } })).StatusCode); // description required
        Assert.Equal(HttpStatusCode.BadRequest, (await sqe.PostAsync(url, new { siteId = site, title = "Die change", supplierId = supplier, details = new { changeType = "Nope", description = "New die" } })).StatusCode);
        var id = (await (await sqe.PostOkAsync(url, new { siteId = site, title = "New casting die", supplierId = supplier, details = new { changeType = "Tooling", description = "Replacement die for cavity 2" } })).JsonAsync()).Id();
        var e = await sqe.GetElAsync($"/api/quality-ops/registers/entries/{id}");
        Assert.Equal("Received", e.GetProperty("entry").Str("status"));
        Assert.Equal(HttpStatusCode.Conflict, (await sqe.PostAsync($"/api/quality-ops/registers/entries/{id}/status", new { status = "Implemented" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await sqe.PostAsync($"/api/quality-ops/registers/entries/{id}/status", new { status = "Under evaluation" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await sqe.PostAsync($"/api/quality-ops/registers/entries/{id}/status", new { status = "Approved" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await qa.PostAsync($"/api/quality-ops/registers/entries/{id}/status", new { status = "Approved" })).StatusCode); // evaluation missing
        e = await sqe.GetElAsync($"/api/quality-ops/registers/entries/{id}");
        Assert.Equal(HttpStatusCode.NoContent, (await sqe.PutAsync($"/api/quality-ops/registers/entries/{id}", new { siteId = site, title = "New casting die", supplierId = supplier,
            details = new { changeType = "Tooling", description = "Replacement die for cavity 2", evaluation = "First article on 5 pieces passed", requalification = true }, rowVersion = e.GetProperty("entry").GetProperty("rowVersion").GetUInt32() })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qa.PostAsync($"/api/quality-ops/registers/entries/{id}/status", new { status = "Approved", comment = "Requalified" })).StatusCode);
        var list = await sqe.GetElAsync(url);
        Assert.Equal(1, list.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Investigation_works_out_of_specification_links_records_and_escalates()
    {
        var site = await app.CreateSiteAsync();
        var (_, lab) = await app.CreateUserAsync(Ops, site);
        var (_, qa) = await app.CreateUserAsync([.. Ops, Permissions.QualityOpsApprove], site);
        var inv = await (await lab.PostOkAsync("/api/quality-ops/investigations", new { siteId = site, kind = 1, title = "Hardness out of limits", scope = "Batch 44 hardness test" })).JsonAsync();
        var id = inv.Id();
        Assert.StartsWith("INV-", inv.Str("number"));
        var row = (await lab.GetElAsync($"/api/quality-ops/investigations/{id}")).GetProperty("investigation");
        Assert.Equal(HttpStatusCode.NoContent, (await lab.PutAsync($"/api/quality-ops/investigations/{id}/work", new { findings = "Retest on a second specimen confirmed 34.2 HRC", sampleReference = "S-44", testMethod = "Rockwell C", lowerLimit = 28m, upperLimit = 32m, result = 34.5m, resultUnit = "HRC", laboratoryErrorFound = false, rowVersion = row.GetProperty("rowVersion").GetUInt32() })).StatusCode);
        row = (await lab.GetElAsync($"/api/quality-ops/investigations/{id}")).GetProperty("investigation");
        Assert.True(row.GetProperty("outOfSpecification").GetBoolean());
        var ev = (await (await lab.PostOkAsync("/api/quality-ops/events", Event(site, "Hardness result high"))).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.NoContent, (await lab.PostAsync($"/api/quality-ops/investigations/{id}/links", new { recordType = "QualityEvent", recordId = ev })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lab.PostAsync($"/api/quality-ops/investigations/{id}/links", new { recordType = "QualityEvent", recordId = ev })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lab.PostAsync($"/api/quality-ops/investigations/{id}/links", new { recordType = "QualityInvestigation", recordId = id })).StatusCode);
        Assert.Equal(1, (await (await lab.PostOkAsync($"/api/quality-ops/investigations/{id}/escalate", new { reason = "Second batch affected" })).JsonAsync()).Int("level"));
        Assert.Equal(1, (await lab.GetElAsync($"/api/quality-ops/investigations/{id}")).GetProperty("linked").GetArrayLength());
        Assert.Equal(HttpStatusCode.NoContent, (await lab.PostAsync($"/api/quality-ops/investigations/{id}/conclude", new { notes = "Heat treat furnace zone 3 under temperature" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lab.PostAsync($"/api/quality-ops/investigations/{id}/close")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await qa.PostAsync($"/api/quality-ops/investigations/{id}/close")).StatusCode);
    }

    [Fact]
    public async Task Analytics_and_service_levels_answer_for_every_module()
    {
        var admin = await app.AdminAsync();
        var from = TestDates.Today.AddDays(-90); var to = TestDates.Today;
        foreach (var mod in (await admin.GetElAsync("/api/quality-ops/analytics/modules")).EnumerateArray())
        {
            var key = mod.Str("key");
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/quality-ops/analytics/aging?module={key}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/quality-ops/analytics/bottlenecks?module={key}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}")).StatusCode);
        }
        foreach (var path in new[] { $"cycle-times?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", $"capa-effectiveness?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", "recurrence", "readiness", "supplier-risk" })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/quality-ops/analytics/{path}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/quality-ops/analytics/aging?module=nope")).StatusCode);

        var cal = (await (await admin.PostOkAsync("/api/quality-ops/calendars", new { name = "Plant " + Guid.NewGuid().ToString("N")[..6], workingDays = new[] { 1, 2, 3, 4, 5 }, workdayStart = "08:00", workdayEnd = "16:00", isDefault = false,
            holidays = new[] { new { date = "2026-12-25", name = "Christmas Day" } } })).JsonAsync()).Id();
        var pol = (await (await admin.PostOkAsync("/api/quality-ops/service-levels", new { name = "Event triage " + Guid.NewGuid().ToString("N")[..6], module = "events", status = "New", targetHours = 8, warningPercent = 75, businessHoursOnly = true, calendarId = cal, active = true })).JsonAsync()).Id();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/quality-ops/service-levels/status")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/quality-ops/service-levels", new { name = "Bad", module = "nope", targetHours = 8, warningPercent = 75, businessHoursOnly = false, active = true })).StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.ServiceLevelPolicies.Where(p => p.Id == pol).ExecuteDeleteAsync();
        await db.CalendarHolidays.Where(h => h.CalendarId == cal).ExecuteDeleteAsync();
        await db.BusinessCalendars.Where(c => c.Id == cal).ExecuteDeleteAsync();
    }

    [Fact]
    public void Skip_lot_state_machine_qualifies_skips_and_disqualifies()
    {
        var r = new SkipLotRule { QualifyAfterLots = 3, InspectEvery = 4, RequalifyAfterLots = 5 };
        SkipLotService.Apply(r, true, true); Assert.Equal(SkipLotState.Qualifying, r.State);
        SkipLotService.Apply(r, true, false); Assert.Equal(SkipLotState.Normal, r.State); Assert.Equal(0, r.ConsecutiveAccepted);
        for (var i = 0; i < 3; i++) SkipLotService.Apply(r, true, true);
        Assert.Equal(SkipLotState.Skipping, r.State);
        SkipLotService.Apply(r, false, null); SkipLotService.Apply(r, false, null);
        Assert.Equal(2, r.LotsSinceInspection);
        SkipLotService.Apply(r, true, false);
        Assert.Equal(SkipLotState.Disqualified, r.State);
        for (var i = 0; i < 4; i++) SkipLotService.Apply(r, true, true);
        Assert.Equal(SkipLotState.Disqualified, r.State);
        SkipLotService.Apply(r, true, true);
        Assert.Equal(SkipLotState.Skipping, r.State);
    }

    [Fact]
    public void Working_hours_skip_nights_weekends_and_holidays()
    {
        var cal = new BusinessCalendar { WorkingDays = "1,2,3,4,5", WorkdayStart = new TimeOnly(8, 0), WorkdayEnd = new TimeOnly(16, 0) };
        cal.Holidays.Add(new CalendarHoliday { Date = new DateOnly(2026, 12, 28), Name = "Plant shutdown" });
        var zone = TimeZoneInfo.Utc;
        // Friday 2026-12-25 14:00 + 6 working hours: 2 on Friday, Monday is a holiday, 4 on Tuesday -> Tuesday 12:00
        var due = ServiceLevelService.AddWorkingHours(new DateTimeOffset(2026, 12, 25, 14, 0, 0, TimeSpan.Zero), 6, cal, zone);
        Assert.Equal(new DateTimeOffset(2026, 12, 29, 12, 0, 0, TimeSpan.Zero), due);
        // before opening counts from opening time
        Assert.Equal(new DateTimeOffset(2026, 12, 29, 9, 0, 0, TimeSpan.Zero), ServiceLevelService.AddWorkingHours(new DateTimeOffset(2026, 12, 29, 6, 0, 0, TimeSpan.Zero), 1, cal, zone));
        Assert.Equal(new DateTimeOffset(2026, 12, 29, 7, 0, 0, TimeSpan.Zero), ServiceLevelService.AddWorkingHours(new DateTimeOffset(2026, 12, 29, 6, 0, 0, TimeSpan.Zero), 1, null, zone));
    }

    [Fact]
    public void Severity_comes_from_the_matrix_cell()
    {
        var s = QualityEventService.Defaults;
        Assert.Equal("Low", QualityEventService.Rate(s, 1, 4).Name);
        Assert.Equal("Medium", QualityEventService.Rate(s, 1, 5).Name);
        Assert.Equal("High", QualityEventService.Rate(s, 2, 5).Name);
        Assert.Equal("Critical", QualityEventService.Rate(s, 4, 4).Name);
        Assert.Equal(25, s.Cells.Count);
    }
}
