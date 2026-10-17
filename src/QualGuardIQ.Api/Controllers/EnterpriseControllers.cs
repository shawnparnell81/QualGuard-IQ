using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Enterprise;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

public sealed record UserIdsRequest(List<Guid> UserIds);
public sealed record ConclusionRequest(string Conclusion);

/// <summary>Multi-site governance: organization tree, corporate standards and rollout, rollups, war room, governance dashboards, scoped broadcasts.</summary>
[ApiController, Authorize, Route("api/enterprise")]
public sealed class EnterpriseController(EnterpriseService svc, GovernanceService gov) : ControllerBase
{
    [HttpGet("units")] [RequirePermission(Permissions.EnterpriseView)] public Task<List<OrgUnit>> Units(CancellationToken ct) => svc.UnitsAsync(ct);
    [HttpPost("units")] [RequirePermission(Permissions.AdminOrganization)] public async Task<object> CreateUnit(OrgUnitInput r, CancellationToken ct) => new { (await svc.CreateUnitAsync(r, ct)).Id };
    [HttpPut("units/{id:guid}")] [RequirePermission(Permissions.AdminOrganization)] public async Task<IActionResult> UpdateUnit(Guid id, OrgUnitInput r, CancellationToken ct) { await svc.UpdateUnitAsync(id, r, ct); return NoContent(); }
    [HttpPost("units/{id:guid}/archive")] [RequirePermission(Permissions.AdminOrganization)] public async Task<IActionResult> ArchiveUnit(Guid id, CancellationToken ct) { await svc.ArchiveUnitAsync(id, ct); return NoContent(); }

    /// <summary>Per-site metrics for every site in scope.</summary>
    [HttpGet("site-metrics")] [RequirePermission(Permissions.EnterpriseView)] public Task<List<SiteMetrics>> SiteMetrics(CancellationToken ct) => svc.SiteMetricsAsync(null, ct);
    /// <summary>Plant, regional and corporate rollups along the organization tree.</summary>
    [HttpGet("rollup")] [RequirePermission(Permissions.EnterpriseView)] public Task<List<UnitRollup>> Rollup(CancellationToken ct) => svc.RollupAsync(ct);
    /// <summary>Corporate, regional or plant governance dashboard: risk, cost, compliance, training, supplier and customer quality.</summary>
    [HttpGet("governance")] [RequirePermission(Permissions.EnterpriseView)] public Task<object> Governance([FromQuery] Guid? unitId, CancellationToken ct) => svc.GovernanceAsync(unitId, ct);
    /// <summary>Plant quality war room with drill-down lists.</summary>
    [HttpGet("war-room/{siteId:guid}")] [RequirePermission(Permissions.EnterpriseView)] public Task<object> WarRoom(Guid siteId, CancellationToken ct) => svc.WarRoomAsync(siteId, ct);

    [HttpGet("standards")] [RequirePermission(Permissions.EnterpriseView)] public Task<List<CorporateStandard>> Standards(CancellationToken ct) => svc.StandardsAsync(ct);
    [HttpPost("standards")] [RequirePermission(Permissions.EnterpriseEdit)] public async Task<object> CreateStandard(StandardInput r, CancellationToken ct) { var s = await svc.CreateStandardAsync(r, ct); return new { s.Id, s.Number }; }
    [HttpGet("standards/{id:guid}/rollouts")] public Task<List<StandardRollout>> Rollouts(Guid id, CancellationToken ct) => svc.RolloutsAsync(id, ct);
    [HttpPost("standards/{id:guid}/rollouts")] public async Task<object> RollOut(Guid id, RolloutInput r, CancellationToken ct) => new { Created = await svc.RollOutAsync(id, r, ct) };
    [HttpGet("rollouts/{id:guid}")] public Task<object> Rollout(Guid id, CancellationToken ct) => svc.RolloutStatusAsync(id, ct);
    [HttpPost("rollouts/{id:guid}/adopt")] public async Task<IActionResult> Adopt(Guid id, RolloutAdoptInput r, CancellationToken ct) { await svc.AdoptAsync(id, r, ct); return NoContent(); }
    [HttpPost("rollouts/{id:guid}/verify")] public async Task<IActionResult> Verify(Guid id, NotesRequest r, CancellationToken ct) { await svc.VerifyRolloutAsync(id, r.Notes, ct); return NoContent(); }

    /// <summary>Site, regional or corporate notice to people and enabled channels in scope.</summary>
    [HttpPost("broadcast")] [RequirePermission(Permissions.EnterpriseEdit)] public Task<object> Broadcast(BroadcastInput r, CancellationToken ct) => gov.BroadcastAsync(r, ct);
}

/// <summary>Skills, certifications, curricula, skills matrix and qualification checks (extends training).</summary>
[ApiController, Authorize, Route("api/training")]
public sealed class WorkforceController(WorkforceService svc, QualificationService q, ModuleSupport m) : ControllerBase
{
    [HttpGet("skills")] [RequirePermission(Permissions.TrainingView)] public Task<List<Skill>> Skills(CancellationToken ct) => svc.SkillsAsync(ct);
    [HttpPost("skills")] [RequirePermission(Permissions.TrainingEdit)] public async Task<object> CreateSkill(SkillInput r, CancellationToken ct) => new { (await svc.CreateSkillAsync(r, ct)).Id };
    [HttpPut("skills/{id:guid}")] public async Task<IActionResult> UpdateSkill(Guid id, SkillInput r, CancellationToken ct) { await svc.UpdateSkillAsync(id, r, ct); return NoContent(); }
    [HttpPost("skills/{id:guid}/requirements")] public async Task<IActionResult> Require(Guid id, SkillRequirementInput r, CancellationToken ct) { await svc.AddRequirementAsync(id, r, ct); return NoContent(); }
    [HttpPost("skills/{id:guid}/certifications")] public async Task<object> Certify(Guid id, CertificationInput r, CancellationToken ct) => new { (await svc.CertifyAsync(id, r, ct)).Id };
    [HttpGet("certifications")] [RequirePermission(Permissions.TrainingView)] public Task<List<EmployeeCertification>> Certifications([FromQuery] Guid? userId, CancellationToken ct) => svc.CertificationsAsync(userId, ct);
    [HttpPost("certifications/{id:guid}/revoke")] public async Task<IActionResult> Revoke(Guid id, ReasonRequest r, CancellationToken ct) { await svc.RevokeAsync(id, r.Reason, ct); return NoContent(); }

    [HttpGet("skills-matrix")] [RequirePermission(Permissions.TrainingView)]
    public Task<SkillsMatrix> Matrix([FromQuery] Guid siteId, [FromQuery] Guid? departmentId, [FromQuery] Guid? workCenterId, CancellationToken ct) => svc.MatrixAsync(siteId, departmentId, workCenterId, ct);
    /// <summary>Qualification gap report for a site, department or production line.</summary>
    [HttpGet("gaps")] [RequirePermission(Permissions.TrainingView)]
    public Task<List<QualificationGap>> Gaps([FromQuery] Guid siteId, [FromQuery] Guid? departmentId, [FromQuery] Guid? workCenterId, CancellationToken ct) => svc.GapsAsync(siteId, departmentId, workCenterId, ct);
    /// <summary>Is this person currently qualified for the skill? Used before assigning work or approvals.</summary>
    [HttpGet("qualification-check")] [RequirePermission(Permissions.TrainingView)]
    public async Task<QualificationResult> Check([FromQuery] Guid userId, [FromQuery] Guid skillId, CancellationToken ct) { await m.ViewAsync(WorkforceService.SkillType, skillId, ct); return await q.CheckAsync(userId, skillId, ct); }

    [HttpGet("curricula")] [RequirePermission(Permissions.TrainingView)] public Task<List<TrainingCurriculum>> Curricula(CancellationToken ct) => svc.CurriculaAsync(ct);
    [HttpPost("curricula")] [RequirePermission(Permissions.TrainingEdit)] public async Task<object> CreateCurriculum(CurriculumInput r, CancellationToken ct) { var c = await svc.CreateCurriculumAsync(r, ct); return new { c.Id, c.Number }; }
    [HttpPost("curricula/{id:guid}/assign")] public async Task<object> AssignCurriculum(Guid id, UserIdsRequest r, CancellationToken ct) => new { Assigned = await svc.AssignCurriculumAsync(id, r.UserIds, ct) };
    /// <summary>Runs automatic curriculum assignment and refresher assignment now (also runs nightly).</summary>
    [HttpPost("auto-assign")] [RequirePermission(Permissions.TrainingEdit)]
    public async Task<object> AutoAssign(CancellationToken ct) { var (a, r) = await svc.AutoAssignAsync(ct); return new { Assigned = a, Refreshers = r }; }
}

/// <summary>Audit programs, annual plans, schedules, teams and auditor qualification.</summary>
[ApiController, Authorize, Route("api/audits/programs")]
public sealed class AuditProgramsController(AuditProgramService svc, QualificationService q) : ControllerBase
{
    [HttpGet] [RequirePermission(Permissions.AuditsView)] public Task<List<AuditProgram>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost] [RequirePermission(Permissions.AuditsEdit)] public async Task<object> Create(AuditProgramInput r, CancellationToken ct) { var p = await svc.CreateAsync(r, ct); return new { p.Id, p.Number }; }
    [HttpGet("{id:guid}")] public async Task<object> Get(Guid id, CancellationToken ct) { var (p, items) = await svc.GetAsync(id, ct); return new { Program = p, Items = items, AuditTypes = AuditProgramService.AuditTypes }; }
    [HttpPost("{id:guid}/items")] public async Task<object> AddItem(Guid id, PlanItemInput r, CancellationToken ct) => new { (await svc.AddItemAsync(id, r, ct)).Id };
    [HttpPut("items/{itemId:guid}")] public async Task<IActionResult> UpdateItem(Guid itemId, PlanItemInput r, CancellationToken ct) { await svc.UpdateItemAsync(itemId, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/approve")] public async Task<IActionResult> Approve(Guid id, CancellationToken ct) { await svc.ApproveAsync(id, ct); return NoContent(); }
    [HttpPost("items/{itemId:guid}/schedule")] public async Task<object> Schedule(Guid itemId, ScheduleAuditInput r, CancellationToken ct) { var a = await svc.ScheduleAsync(itemId, r, ct); return new { a.Id, a.Number }; }
    [HttpGet("~/api/audits/auditor-check")] [RequirePermission(Permissions.AuditsView)]
    public Task<QualificationResult> AuditorCheck([FromQuery] Guid userId, [FromQuery] string auditType, [FromQuery] Guid siteId, CancellationToken ct) => q.CheckAuditorAsync(userId, auditType, siteId, ct);
}

[ApiController, Authorize, Route("api/compliance")]
public sealed class ComplianceController(ComplianceService svc) : ControllerBase
{
    [HttpGet("packs")] [RequirePermission(Permissions.ComplianceView)] public Task<object> Packs([FromQuery] Guid siteId, CancellationToken ct) => svc.PacksAsync(siteId, ct);
    /// <summary>Clause numbers and short titles of a pack (no standard text).</summary>
    [HttpGet("packs/{code}")] [RequirePermission(Permissions.ComplianceView)]
    public object Pack(string code) => ComplianceService.Packs.FirstOrDefault(p => p.Code == code) ?? throw new NotFoundException("Compliance pack");
    [HttpPost("packs/{code}/enable")] [RequirePermission(Permissions.ComplianceEdit)]
    public async Task<object> Enable(string code, [FromQuery] Guid siteId, EnableRequest r, CancellationToken ct) => new { Created = await svc.EnablePackAsync(code, siteId, r.Enabled, ct) };
    [HttpGet("obligations")] [RequirePermission(Permissions.ComplianceView)]
    public Task<List<ObligationRow>> Obligations([FromQuery] Guid? siteId, [FromQuery] string? pack, [FromQuery] ObligationSource? source, [FromQuery] bool unmappedOnly = false, CancellationToken ct = default) => svc.ListAsync(siteId, pack, source, unmappedOnly, ct);
    [HttpPost("obligations")] [RequirePermission(Permissions.ComplianceEdit)] public async Task<object> Create(ObligationInput r, CancellationToken ct) { var o = await svc.CreateAsync(r, ct); return new { o.Id, o.Number }; }
    [HttpPost("obligations/{id:guid}/mappings")] public async Task<IActionResult> Map(Guid id, MappingInput r, CancellationToken ct) { await svc.MapAsync(id, r, ct); return NoContent(); }
    [HttpDelete("obligations/{id:guid}/mappings/{type}/{targetId:guid}")] public async Task<IActionResult> Unmap(Guid id, string type, Guid targetId, CancellationToken ct) { await svc.UnmapAsync(id, type, targetId, ct); return NoContent(); }
    [HttpGet("coverage")] [RequirePermission(Permissions.ComplianceView)] public Task<object> Coverage([FromQuery] Guid siteId, CancellationToken ct) => svc.CoverageAsync(siteId, ct);
}

[ApiController, Authorize, Route("api/validation")]
public sealed class ValidationController(ValidationService svc) : ControllerBase
{
    public sealed record ResolveRequest(string Resolution, bool Retest = true);
    [HttpGet] [RequirePermission(Permissions.ValidationView)] public Task<List<ValidationProject>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost] [RequirePermission(Permissions.ValidationEdit)] public async Task<object> Create(ValidationInput r, CancellationToken ct) { var v = await svc.CreateAsync(r, ct); return new { v.Id, v.Number }; }
    [HttpGet("{id:guid}")] public Task<object> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPut("{id:guid}/tests")] public async Task<IActionResult> Tests(Guid id, List<ValidationTestInput> r, CancellationToken ct) { await svc.SaveTestsAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/submit-protocol")] public async Task<IActionResult> SubmitProtocol(Guid id, CancellationToken ct) { await svc.SubmitProtocolAsync(id, ct); return NoContent(); }
    [HttpPost("{id:guid}/tests/{testId:guid}/execute")] public async Task<IActionResult> Execute(Guid id, Guid testId, ExecuteTestInput r, CancellationToken ct) { await svc.ExecuteAsync(id, testId, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/deviations")] public async Task<object> Deviation(Guid id, DeviationInput r, CancellationToken ct) => new { (await svc.AddDeviationAsync(id, r, ct)).Id };
    [HttpPost("{id:guid}/deviations/{devId:guid}/resolve")] public async Task<IActionResult> Resolve(Guid id, Guid devId, ResolveRequest r, CancellationToken ct) { await svc.ResolveDeviationAsync(id, devId, new ResolveDeviationInput(r.Resolution), r.Retest, ct); return NoContent(); }
    [HttpPost("{id:guid}/submit-results")] public async Task<IActionResult> SubmitResults(Guid id, ConclusionRequest r, CancellationToken ct) { await svc.SubmitResultsAsync(id, r.Conclusion, ct); return NoContent(); }
}

/// <summary>Project quality planning / APQP phase gates, PPAP and capability studies.</summary>
[ApiController, Authorize, Route("api/launch")]
public sealed class LaunchController(ProjectService svc) : ControllerBase
{
    [HttpGet("projects")] [RequirePermission(Permissions.ApqpView)] public Task<List<QualityProject>> Projects(CancellationToken ct) => svc.ProjectsAsync(ct);
    [HttpPost("projects")] [RequirePermission(Permissions.ApqpEdit)] public async Task<object> CreateProject(ProjectInput r, CancellationToken ct) { var p = await svc.CreateProjectAsync(r, ct); return new { p.Id, p.Number }; }
    [HttpGet("projects/{id:guid}")] public Task<object> Project(Guid id, CancellationToken ct) => svc.ProjectAsync(id, ct);
    [HttpGet("projects/{id:guid}/readiness")] public Task<object> Readiness(Guid id, CancellationToken ct) => svc.ReadinessReportAsync(id, ct);
    [HttpPost("projects/{id:guid}/deliverables")] public async Task<object> AddDeliverable(Guid id, DeliverableInput r, CancellationToken ct) => new { (await svc.AddDeliverableAsync(id, r, ct)).Id };
    [HttpPut("projects/{id:guid}/deliverables/{deliverableId:guid}")] public async Task<IActionResult> UpdateDeliverable(Guid id, Guid deliverableId, DeliverableUpdate r, CancellationToken ct) { await svc.UpdateDeliverableAsync(id, deliverableId, r, ct); return NoContent(); }
    [HttpPost("projects/{id:guid}/gate")] public async Task<IActionResult> Gate(Guid id, GateInput r, CancellationToken ct) { await svc.GateAsync(id, r, ct); return NoContent(); }

    [HttpGet("ppap")] [RequirePermission(Permissions.ApqpView)] public Task<List<PpapSubmission>> Ppaps(CancellationToken ct) => svc.PpapsAsync(ct);
    [HttpPost("ppap")] [RequirePermission(Permissions.ApqpEdit)] public async Task<object> CreatePpap(PpapInput r, CancellationToken ct) { var p = await svc.CreatePpapAsync(r, ct); return new { p.Id, p.Number }; }
    [HttpGet("ppap/{id:guid}")] public Task<object> Ppap(Guid id, CancellationToken ct) => svc.PpapAsync(id, ct);
    [HttpPut("ppap/{id:guid}/elements/{code:int}")] public async Task<IActionResult> Element(Guid id, int code, PpapElementUpdate r, CancellationToken ct) { await svc.UpdateElementAsync(id, code, r, ct); return NoContent(); }
    [HttpPost("ppap/{id:guid}/submit")] public async Task<IActionResult> SubmitPpap(Guid id, CancellationToken ct) { await svc.SubmitPpapAsync(id, ct); return NoContent(); }
    [HttpPost("ppap/{id:guid}/customer-decision")] public async Task<IActionResult> CustomerDecision(Guid id, CustomerDecisionInput r, CancellationToken ct) { await svc.CustomerDecisionAsync(id, r, ct); return NoContent(); }

    [HttpGet("capability")] [RequirePermission(Permissions.ApqpView)] public Task<List<CapabilityStudy>> Capability([FromQuery] Guid? partId, CancellationToken ct) => svc.CapabilityStudiesAsync(partId, ct);
    [HttpPost("capability")] [RequirePermission(Permissions.ApqpEdit)] public async Task<object> CreateCapability(CapabilityInput r, CancellationToken ct) { var c = await svc.CreateCapabilityAsync(r, ct); return new { c.Id, c.Number, c.Cp, c.Cpk, c.Pp, c.Ppk }; }
    /// <summary>Calculator: Cp/Cpk/Pp/Ppk for the given data without saving.</summary>
    [HttpPost("capability/calculate")] [RequirePermission(Permissions.ApqpView)]
    public object Calculate(CapabilityInput r) { var c = ProjectService.Compute(new CapabilityStudy { Lsl = r.Lsl, Usl = r.Usl, SubgroupSize = r.SubgroupSize, Values = r.Values }); return new { c.Mean, c.SigmaWithin, c.SigmaOverall, c.Cp, c.Cpk, c.Pp, c.Ppk }; }
}

[ApiController, Authorize, Route("api/governance")]
public sealed class GovernanceController(GovernanceService svc) : ControllerBase
{
    [HttpGet("retention/policies")] public Task<List<RetentionPolicy>> Policies(CancellationToken ct) => svc.PoliciesAsync(ct);
    [HttpPost("retention/policies")] public async Task<object> CreatePolicy(RetentionPolicyInput r, CancellationToken ct) => new { (await svc.SavePolicyAsync(null, r, ct)).Id };
    [HttpPut("retention/policies/{id:guid}")] public async Task<IActionResult> UpdatePolicy(Guid id, RetentionPolicyInput r, CancellationToken ct) { await svc.SavePolicyAsync(id, r, ct); return NoContent(); }
    [HttpGet("retention/policies/{id:guid}/due")] public Task<List<RetentionDue>> Due(Guid id, CancellationToken ct) => svc.DueAsync(id, ct);
    [HttpPost("retention/policies/{id:guid}/decisions")] public async Task<IActionResult> Decide(Guid id, RetentionDecisionInput r, CancellationToken ct) { await svc.DecideAsync(id, r, ct); return NoContent(); }
    [HttpGet("retention/decisions")] public Task<List<RetentionDecision>> Decisions(CancellationToken ct) => svc.DecisionsAsync(ct);
    [HttpGet("retention/record-types")] [RequirePermission(Permissions.GovernanceView)] public string[] RecordTypes() => GovernanceService.RetentionTypes;

    [HttpGet("continuity")] [RequirePermission(Permissions.GovernanceView)] public Task<List<ContinuityExercise>> Exercises(CancellationToken ct) => svc.ExercisesAsync(ct);
    [HttpPost("continuity")] [RequirePermission(Permissions.GovernanceEdit)] public async Task<object> Plan(ContinuityInput r, CancellationToken ct) { var e = await svc.PlanExerciseAsync(r, ct); return new { e.Id, e.Number }; }
    [HttpPost("continuity/{id:guid}/result")] public async Task<IActionResult> Result(Guid id, ContinuityResultInput r, CancellationToken ct) { await svc.RecordResultAsync(id, r, ct); return NoContent(); }
    [HttpGet("system-checks")] [RequirePermission(Permissions.GovernanceView)] public Task<Dictionary<string, object?>> Checks(CancellationToken ct) => svc.SystemChecksAsync(ct);
    /// <summary>Part 11 readiness evidence and electronic signature integrity check.</summary>
    [HttpGet("part11")] [RequirePermission(Permissions.AuditTrailView)] public Task<object> Part11(CancellationToken ct) => svc.Part11Async(ct);
}

/// <summary>Public API clients (API keys and OAuth client credentials) and notification channels.</summary>
[ApiController, Authorize, Route("api/admin")]
[RequirePermission(Permissions.ApiAdmin)]
public sealed class ApiAdminController(GovernanceService svc) : ControllerBase
{
    public sealed record TestRequest(bool EnableOnSuccess);
    [HttpGet("api-clients")] public Task<List<object>> Clients(CancellationToken ct) => svc.ClientsAsync(ct);
    /// <summary>Creates a client. The secret is shown once in the response and stored only as a hash.</summary>
    [HttpPost("api-clients")] public Task<ApiClientCreated> Create(ApiClientInput r, CancellationToken ct) => svc.CreateClientAsync(r, ct);
    [HttpPost("api-clients/{id:guid}/enabled")] public async Task<IActionResult> Enable(Guid id, EnableRequest r, CancellationToken ct) { await svc.SetClientEnabledAsync(id, r.Enabled, ct); return NoContent(); }
    [HttpPost("api-clients/{id:guid}/rotate")] public Task<ApiClientCreated> Rotate(Guid id, CancellationToken ct) => svc.RotateAsync(id, ct);

    [HttpGet("notification-channels")] public Task<List<NotificationChannel>> Channels(CancellationToken ct) => svc.ChannelsAsync(ct);
    [HttpPost("notification-channels")] public async Task<object> CreateChannel(ChannelInput r, CancellationToken ct) => new { (await svc.SaveChannelAsync(null, r, ct)).Id };
    [HttpPut("notification-channels/{id:guid}")] public async Task<IActionResult> UpdateChannel(Guid id, ChannelInput r, CancellationToken ct) { await svc.SaveChannelAsync(id, r, ct); return NoContent(); }
    [HttpPost("notification-channels/{id:guid}/test")] public async Task<object> Test(Guid id, TestRequest r, CancellationToken ct) => new { Ok = await svc.TestChannelAsync(id, r.EnableOnSuccess, ct) };
    [HttpPost("notification-channels/{id:guid}/disable")] public async Task<IActionResult> Disable(Guid id, CancellationToken ct) { await svc.DisableChannelAsync(id, ct); return NoContent(); }
}

/// <summary>Document change impact and change-request impact evaluation.</summary>
[ApiController, Authorize]
public sealed class ImpactController(ImpactService svc) : ControllerBase
{
    [HttpGet("api/documents/{id:guid}/impact")] public Task<object> Document(Guid id, CancellationToken ct) => svc.DocumentImpactAsync(id, ct);
    [HttpGet("api/changes/{id:guid}/impact")] public Task<List<ImpactArea>> Change(Guid id, CancellationToken ct) => svc.ChangeImpactAsync(id, ct);
    /// <summary>Adds the evaluated items to the change's affected items; each then needs completion or a formal waiver.</summary>
    [HttpPost("api/changes/{id:guid}/impact/apply")] public async Task<object> Apply(Guid id, CancellationToken ct) => new { Added = await svc.ApplyChangeImpactAsync(id, ct) };
}
