using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Platform;

namespace QualGuardIQ.Api.Controllers;

public sealed record WebhookEnabledRequest(bool Enabled);

/// <summary>Platform services: event bus, webhooks, activity stream, genealogy, calendar, resources, personal workspace, operations.</summary>
[ApiController, Authorize, Route("api/platform")]
public sealed class PlatformController(PlatformService svc) : ControllerBase
{
    [HttpGet("events/types")] public string[] EventTypes() => PlatformEvents.Subscribable;
    [HttpPost("events")] public async Task<object> Publish(ExternalEventInput r, CancellationToken ct) { var e = await svc.PublishExternalAsync(r, ct); return new { e.Id }; }
    [HttpPost("events/pump")] [RequirePermission(Permissions.PlatformAdmin)] public async Task<object> Pump(CancellationToken ct) => new { Published = await svc.PumpAsync(ct), Delivered = await svc.DeliverAsync(ct) };
    [HttpGet("activity")] public Task<List<PlatformEvent>> Activity([FromQuery] string? type, [FromQuery] string? recordType, [FromQuery] Guid? userId, [FromQuery] DateTimeOffset? before, [FromQuery] int take = 100, CancellationToken ct = default) => svc.ActivityAsync(type, recordType, userId, before, take, ct);

    [HttpGet("webhooks")] public Task<List<object>> Webhooks(CancellationToken ct) => svc.WebhooksAsync(ct);
    [HttpPost("webhooks")] public async Task<object> CreateWebhook(WebhookInput r, CancellationToken ct) { var (s, secret) = await svc.CreateWebhookAsync(r, ct); return new { s.Id, Secret = secret }; }
    [HttpPost("webhooks/{id:guid}/enabled")] public async Task<IActionResult> Enable(Guid id, WebhookEnabledRequest r, CancellationToken ct) { await svc.SetWebhookEnabledAsync(id, r.Enabled, ct); return NoContent(); }
    [HttpGet("webhooks/{id:guid}/deliveries")] public Task<List<WebhookDelivery>> Deliveries(Guid id, CancellationToken ct) => svc.DeliveriesAsync(id, ct);

    [HttpGet("genealogy/{type}/{id:guid}")] public Task<object> Genealogy(string type, Guid id, [FromQuery] int depth = 4, CancellationToken ct = default) => svc.GenealogyAsync(type, id, depth, ct);
    [HttpGet("calendar")] public Task<List<CalendarItem>> Calendar([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? siteId, CancellationToken ct) => svc.CalendarAsync(from, to, siteId, ct);
    [HttpGet("resources")] public Task<object> Resources([FromQuery] Guid siteId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) => svc.ResourcesAsync(siteId, from, to, ct);
    [HttpPost("resources/absences")] public async Task<object> Absence(AbsenceInput r, CancellationToken ct) => new { (await svc.AddAbsenceAsync(r, ct)).Id };
    [HttpGet("my")] public Task<object> My(CancellationToken ct) => svc.MyWorkspaceAsync(ct);

    [HttpGet("health")] public Task<object> Health(CancellationToken ct) => svc.HealthAsync(ct);
    [HttpGet("security")] public Task<object> Security(CancellationToken ct) => svc.SecurityOpsAsync(ct);
    [HttpGet("modules")] public object Modules() => svc.Modules();
}

/// <summary>Low-code report and dashboard designers and saved searches.</summary>
[ApiController, Authorize, Route("api/designer"), RequirePermission(Permissions.ReportsView)]
public sealed class DesignerController(DesignerService svc) : ControllerBase
{
    [HttpGet("reports")] public Task<List<SavedReport>> Reports(CancellationToken ct) => svc.ReportsAsync(ct);
    [HttpPost("reports")] public async Task<object> CreateReport(SavedReportInput r, CancellationToken ct) => new { (await svc.CreateReportAsync(r, ct)).Id };
    [HttpPut("reports/{id:guid}")] public async Task<IActionResult> UpdateReport(Guid id, SavedReportInput r, CancellationToken ct) { await svc.UpdateReportAsync(id, r, ct); return NoContent(); }
    [HttpDelete("reports/{id:guid}")] public async Task<IActionResult> DeleteReport(Guid id, CancellationToken ct) { await svc.DeleteReportAsync(id, ct); return NoContent(); }
    [HttpGet("reports/{id:guid}/run")] public Task<SavedReportRun> Run(Guid id, CancellationToken ct) => svc.RunReportAsync(id, ct);

    [HttpGet("schedules")] public Task<List<ReportSchedule>> Schedules(CancellationToken ct) => svc.SchedulesAsync(ct);
    [HttpPost("schedules")] public async Task<object> Schedule(ScheduleInput r, CancellationToken ct) { var s = await svc.CreateScheduleAsync(r, ct); return new { s.Id, s.NextRunAt }; }
    [HttpDelete("schedules/{id:guid}")] public async Task<IActionResult> Unschedule(Guid id, CancellationToken ct) { await svc.DeleteScheduleAsync(id, ct); return NoContent(); }

    [HttpGet("dashboards")] public Task<List<Dashboard>> Dashboards(CancellationToken ct) => svc.DashboardsAsync(ct);
    [HttpPost("dashboards")] public async Task<object> CreateDashboard(DashboardInput r, CancellationToken ct) => new { (await svc.SaveDashboardAsync(null, r, ct)).Id };
    [HttpPut("dashboards/{id:guid}")] public async Task<IActionResult> UpdateDashboard(Guid id, DashboardInput r, CancellationToken ct) { await svc.SaveDashboardAsync(id, r, ct); return NoContent(); }
    [HttpGet("dashboards/{id:guid}/data")] public Task<object> DashboardData(Guid id, CancellationToken ct) => svc.DashboardDataAsync(id, ct);

    [HttpGet("searches")] public Task<List<SavedSearch>> Searches(CancellationToken ct) => svc.SearchesAsync(ct);
    [HttpPost("searches")] public async Task<object> SaveSearch(SavedSearchInput r, CancellationToken ct) => new { (await svc.SaveSearchAsync(r, ct)).Id };
    [HttpDelete("searches/{id:guid}")] public async Task<IActionResult> DeleteSearch(Guid id, CancellationToken ct) { await svc.DeleteSearchAsync(id, ct); return NoContent(); }
}

/// <summary>Form designer platform: reusable component library (versioned, published), dynamic-rule preview.</summary>
[ApiController, Authorize, Route("api/forms/library")]
public sealed class FormLibraryController(FormLibraryService svc) : ControllerBase
{
    [HttpGet] public Task<List<FormComponent>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost] public async Task<object> Create(FormComponentInput r, CancellationToken ct) => new { (await svc.CreateAsync(r, ct)).Id };
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, FormComponentInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/publish")] public async Task<IActionResult> Publish(Guid id, CancellationToken ct) { await svc.PublishAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/new-version")] public async Task<object> NewVersion(Guid id, CancellationToken ct) => new { (await svc.NewVersionAsync(id, ct)).Id };
    [HttpGet("{id:guid}/insert")] public Task<List<QualGuardIQ.Application.Forms.FormField>> Insert(Guid id, [FromQuery] string? prefix, CancellationToken ct) => svc.InsertAsync(id, prefix, ct);
    [HttpPost("preview"), RequirePermission(Permissions.FormsView)] public FormPreviewResult Preview(FormPreviewInput r) => FormLibraryService.Preview(r);
}

/// <summary>Business rule engine administration: validation, routing, escalation and automation rules, dry runs and the firing log.</summary>
[ApiController, Authorize, Route("api/platform/rules"), RequirePermission(Permissions.PlatformAdmin)]
public sealed class RulesController(RuleEngine svc) : ControllerBase
{
    [HttpGet] public Task<List<BusinessRule>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost] public async Task<object> Create(BusinessRuleInput r, CancellationToken ct) => new { (await svc.CreateAsync(r, ct)).Id };
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, BusinessRuleInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
    [HttpGet("{id:guid}/test/{recordId:guid}")] public Task<RuleTestResult> Test(Guid id, Guid recordId, CancellationToken ct) => svc.TestAsync(id, recordId, ct);
    [HttpGet("firings")] public Task<List<RuleFiring>> Firings([FromQuery] Guid? ruleId, CancellationToken ct) => svc.FiringsAsync(ruleId, ct);
    [HttpPost("escalate")] public async Task<object> Escalate(CancellationToken ct) => new { Escalated = await svc.EscalateAsync(ct) };
}

/// <summary>Enterprise data governance and master data management.</summary>
[ApiController, Authorize, Route("api/governance/data")]
public sealed class DataGovernanceController(DataGovernanceService svc) : ControllerBase
{
    [HttpGet("domains")] public Task<object> Overview(CancellationToken ct) => svc.OverviewAsync(ct);
    [HttpPut("domains/{key}"), RequirePermission(Permissions.PlatformAdmin)] public async Task<IActionResult> UpdateDomain(string key, DataDomainInput r, CancellationToken ct) { await svc.UpdateDomainAsync(key, r, ct); return NoContent(); }
    [HttpGet("rules"), RequirePermission(Permissions.MasterDataView)] public Task<List<DataQualityRule>> Rules(CancellationToken ct) => svc.RulesAsync(ct);
    [HttpPost("rules"), RequirePermission(Permissions.PlatformAdmin)] public async Task<object> AddRule(DataRuleInput r, CancellationToken ct) => new { (await svc.AddRuleAsync(r, ct)).Id };
    [HttpDelete("rules/{id:guid}"), RequirePermission(Permissions.PlatformAdmin)] public async Task<IActionResult> DeleteRule(Guid id, CancellationToken ct) { await svc.DeleteRuleAsync(id, ct); return NoContent(); }
    [HttpGet("quality/{key}"), RequirePermission(Permissions.PlatformAdmin)] public Task<DomainQuality> Evaluate(string key, CancellationToken ct) => svc.EvaluateAsync(key, ct);
    [HttpPost("quality/run"), RequirePermission(Permissions.PlatformAdmin)] public Task<List<DomainQuality>> Run(CancellationToken ct) => svc.RunAsync(ct);
    [HttpGet("changes")] public Task<List<MasterDataChange>> Changes([FromQuery] string? domain, CancellationToken ct) => svc.ChangesAsync(domain, ct);
    [HttpPost("changes")] public async Task<object> RequestChange(MasterChangeInput r, CancellationToken ct) { var c = await svc.RequestAsync(r, ct); return new { c.Id, c.Number }; }
}

/// <summary>Disaster recovery center: backups, recovery objectives and procedures, recoverability status, restore fingerprint.</summary>
[ApiController, Authorize, Route("api/platform/dr"), RequirePermission(Permissions.PlatformAdmin)]
public sealed class RecoveryController(RecoveryService svc) : ControllerBase
{
    [HttpGet("status")] public Task<object> Status(CancellationToken ct) => svc.StatusAsync(ct);
    [HttpGet("fingerprint")] public Task<object> Fingerprint(CancellationToken ct) => svc.FingerprintAsync(ct);
    [HttpGet("backups")] public Task<List<BackupRecord>> Backups(CancellationToken ct) => svc.BackupsAsync(ct);
    [HttpPost("backups")] public async Task<object> Register(BackupInput r, CancellationToken ct) => new { (await svc.RegisterAsync(r, ct)).Id };
    [HttpPost("backups/{id:guid}/verify")] public async Task<IActionResult> Verify(Guid id, BackupVerifyInput r, CancellationToken ct) { await svc.VerifyAsync(id, r, ct); return NoContent(); }
    [HttpGet("objectives")] public Task<List<RecoveryObjective>> Objectives(CancellationToken ct) => svc.ObjectivesAsync(ct);
    [HttpPost("objectives")] public async Task<object> AddObjective(ObjectiveInput r, CancellationToken ct) => new { (await svc.SaveObjectiveAsync(null, r, ct)).Id };
    [HttpPut("objectives/{id:guid}")] public async Task<IActionResult> UpdateObjective(Guid id, ObjectiveInput r, CancellationToken ct) { await svc.SaveObjectiveAsync(id, r, ct); return NoContent(); }
}

/// <summary>Marketplace: add-ons, quality extensions and industry packs with dependencies, versions and signed licenses.</summary>
[ApiController, Authorize, Route("api/platform/marketplace"), RequirePermission(Permissions.PlatformAdmin)]
public sealed class MarketplaceController(MarketplaceService svc) : ControllerBase
{
    [HttpGet] public Task<object> Catalog(CancellationToken ct) => svc.CatalogAsync(ct);
    [HttpPost("packages")] public async Task<object> Import(ExtensionManifest r, CancellationToken ct) { var p = await svc.ImportAsync(r, ct); return new { p.Id, p.Key, p.Version }; }
    [HttpPost("{key}/install")] public async Task<object> Install(string key, [FromQuery] string? version, CancellationToken ct) { var i = await svc.InstallAsync(key, version, ct); return new { i.Key, i.Version, Items = i.Created.Items }; }
    [HttpPost("{key}/upgrade")] public async Task<object> Upgrade(string key, [FromQuery] string? version, CancellationToken ct) { var i = await svc.UpgradeAsync(key, version, ct); return new { i.Key, i.Version, Items = i.Created.Items }; }
    [HttpPost("{key}/enabled")] public async Task<IActionResult> Enabled(string key, WebhookEnabledRequest r, CancellationToken ct) { await svc.SetEnabledAsync(key, r.Enabled, ct); return NoContent(); }
    [HttpDelete("{key}")] public async Task<IActionResult> Uninstall(string key, CancellationToken ct) { await svc.UninstallAsync(key, ct); return NoContent(); }
    [HttpGet("licenses")] public Task<List<ExtensionLicense>> Licenses(CancellationToken ct) => svc.LicensesAsync(ct);
    [HttpPost("licenses")] public async Task<object> Activate(LicenseActivation r, CancellationToken ct) { var l = await svc.ActivateAsync(r, ct); return new { l.Id, l.LicenseId, l.ExpiresAt, l.Extensions }; }
    [HttpPost("licenses/{id:guid}/revoke")] public async Task<IActionResult> Revoke(Guid id, CancellationToken ct) { await svc.RevokeAsync(id, ct); return NoContent(); }
    [HttpGet("licensing")] public Task<object> Licensing(CancellationToken ct) => svc.LicensingStatusAsync(ct);
    [HttpGet("licensing/revocations")] public Task<List<LicenseRevocation>> Revocations(CancellationToken ct) => svc.RevocationsAsync(ct);
    [HttpPost("licensing/revocations")] public async Task<IActionResult> RevokeId(LicenseRevocationInput r, CancellationToken ct) { await svc.RevokeIdAsync(r, ct); return NoContent(); }
    [HttpGet("licensing/keys")] public Task<List<LicenseSigningKey>> Keys(CancellationToken ct) => svc.SigningKeysAsync(ct);
    [HttpPost("licensing/keys")] public async Task<object> RegisterKey(SigningKeyInput r, CancellationToken ct) { var k = await svc.RegisterKeyAsync(r, ct); return new { k.Id, k.KeyId, k.Fingerprint }; }
    [HttpPost("licensing/keys/{id:guid}/retire")] public async Task<IActionResult> RetireKey(Guid id, CancellationToken ct) { await svc.SetKeyStatusAsync(id, LicenseKeyStatus.Retired, ct); return NoContent(); }
    [HttpPost("licensing/keys/{id:guid}/revoke")] public async Task<IActionResult> RevokeKey(Guid id, CancellationToken ct) { await svc.SetKeyStatusAsync(id, LicenseKeyStatus.Revoked, ct); return NoContent(); }
}

/// <summary>Scheduled report distribution (saved reports and board reports), delivery policy, board report editions and delivered report files.</summary>
[ApiController, Authorize, Route("api/reports/distribution"), RequirePermission(Permissions.ReportsView)]
public sealed class ReportDistributionController(ReportDistributionService svc) : ControllerBase
{
    [HttpGet("schedules")] public Task<List<DistributionScheduleRow>> Schedules([FromQuery] bool all, CancellationToken ct) => svc.SchedulesAsync(all, ct);
    [HttpGet("schedules/{id:guid}")] public Task<DistributionScheduleRow> Schedule(Guid id, CancellationToken ct) => svc.ScheduleAsync(id, ct);
    [HttpPost("schedules")] public async Task<object> Create(DistributionInput r, CancellationToken ct) => new { Id = await svc.CreateAsync(r, ct) };
    [HttpPut("schedules/{id:guid}")] public async Task<IActionResult> Update(Guid id, DistributionInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }
    [HttpDelete("schedules/{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
    [HttpPost("schedules/{id:guid}/pause")] public async Task<IActionResult> Pause(Guid id, PauseInput r, CancellationToken ct) { await svc.PauseAsync(id, r, ct); return NoContent(); }
    [HttpPost("schedules/{id:guid}/resume")] public async Task<IActionResult> Resume(Guid id, CancellationToken ct) { await svc.ResumeAsync(id, ct); return NoContent(); }
    [HttpPost("schedules/{id:guid}/run")] public Task<DistributionRow> RunNow(Guid id, CancellationToken ct) => svc.RunNowAsync(id, ct);
    [HttpGet("schedules/{id:guid}/history")] public Task<List<DistributionRow>> History(Guid id, CancellationToken ct) => svc.HistoryAsync(id, ct);

    [HttpGet("policy")] public Task<ReportDeliveryPolicy> Policy(CancellationToken ct) => svc.PolicyAsync(ct);
    [HttpPut("policy")] public Task<ReportDeliveryPolicy> SavePolicy(PolicyInput r, CancellationToken ct) => svc.SavePolicyAsync(r, ct);

    [HttpGet("files/{id:guid}")]
    public async Task<IActionResult> File(Guid id, CancellationToken ct)
    {
        var (data, name, type) = await svc.ArtifactAsync(id, ct);
        Response.Headers.CacheControl = "private, no-store";
        return File(data, type, name);
    }
}

[ApiController, Authorize, Route("api/enterprise/board-editions"), RequirePermission(Permissions.EnterpriseView)]
public sealed class BoardEditionController(ReportDistributionService svc) : ControllerBase
{
    [HttpGet] public Task<List<EditionRow>> List([FromQuery] string? kind, CancellationToken ct) => svc.EditionsAsync(kind, ct);
    [HttpPost("{kind}")] public Task<EditionRow> Generate(string kind, CancellationToken ct) => svc.GenerateEditionAsync(kind, ct);
    [HttpGet("{id:guid}")] public Task<object> Get(Guid id, CancellationToken ct) => svc.EditionAsync(id, ct);
    [HttpGet("{id:guid}/pdf")] public async Task<IActionResult> Pdf(Guid id, CancellationToken ct) { Response.Headers.CacheControl = "private, no-store"; return File(await svc.EditionPdfAsync(id, ct), "application/pdf", $"board-edition-{id}.pdf"); }
    [HttpPost("{id:guid}/approve")] public async Task<IActionResult> Approve(Guid id, EditionDecision r, CancellationToken ct) { await svc.DecideEditionAsync(id, true, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/reject")] public async Task<IActionResult> Reject(Guid id, EditionDecision r, CancellationToken ct) { await svc.DecideEditionAsync(id, false, r, ct); return NoContent(); }
}
