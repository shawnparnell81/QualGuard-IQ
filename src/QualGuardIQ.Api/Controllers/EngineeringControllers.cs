using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Analytics;
using QualGuardIQ.Infrastructure.Modules.Engineering;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Api.Controllers;

public sealed record OptionalTextRequest(string? Text);
public sealed record PublishRequest(bool Published);
public sealed record DeploymentStatusRequest(DeploymentStatus Status, string? Notes);
public sealed record ChatRequest(List<ChatMessage> Messages);

/// <summary>Requirements and traceability, design reviews, product configuration and BOM quality.</summary>
[ApiController, Authorize, Route("api/engineering")]
public sealed class EngineeringController(EngineeringService svc) : ControllerBase
{
    [HttpGet("requirements")] [RequirePermission(Permissions.EngineeringView)] public Task<List<ProductRequirement>> Requirements([FromQuery] Guid? productId, CancellationToken ct) => svc.RequirementsAsync(productId, ct);
    [HttpPost("requirements")] [RequirePermission(Permissions.EngineeringEdit)] public async Task<object> CreateRequirement(RequirementInput r, CancellationToken ct) { var q = await svc.CreateRequirementAsync(r, ct); return new { q.Id, q.Number }; }
    [HttpGet("requirements/{id:guid}")] public Task<object> Requirement(Guid id, CancellationToken ct) => svc.RequirementAsync(id, ct);
    [HttpPost("requirements/{id:guid}/revise")] public async Task<IActionResult> Revise(Guid id, RequirementRevisionInput r, CancellationToken ct) { await svc.ReviseRequirementAsync(id, r, ct); return NoContent(); }
    [HttpPost("requirements/{id:guid}/approve")] public async Task<IActionResult> Approve(Guid id, CancellationToken ct) { await svc.ApproveRequirementAsync(id, ct); return NoContent(); }
    [HttpPost("requirements/{id:guid}/trace")] public async Task<IActionResult> Trace(Guid id, TraceInput r, CancellationToken ct) { await svc.TraceAsync(id, r, ct); return NoContent(); }
    [HttpDelete("requirements/{id:guid}/trace/{linkId:guid}")] public async Task<IActionResult> Untrace(Guid id, Guid linkId, CancellationToken ct) { await svc.UntraceAsync(id, linkId, ct); return NoContent(); }
    [HttpGet("traceability")] [RequirePermission(Permissions.EngineeringView)] public Task<object> TraceReport([FromQuery] Guid? productId, CancellationToken ct) => svc.TraceReportAsync(productId, ct);
    [HttpGet("trace-targets")] public Dictionary<string, string[]> Targets() => EngineeringService.TraceTargets;

    [HttpGet("reviews")] [RequirePermission(Permissions.EngineeringView)] public Task<List<DesignReview>> Reviews(CancellationToken ct) => svc.ReviewsAsync(ct);
    [HttpPost("reviews")] [RequirePermission(Permissions.EngineeringEdit)] public async Task<object> CreateReview(DesignReviewInput r, CancellationToken ct) { var d = await svc.CreateReviewAsync(r, ct); return new { d.Id, d.Number }; }
    [HttpGet("reviews/{id:guid}")] public Task<object> Review(Guid id, CancellationToken ct) => svc.ReviewAsync(id, ct);
    [HttpPost("reviews/{id:guid}/findings")] public async Task<object> Finding(Guid id, FindingInput r, CancellationToken ct) => new { (await svc.AddFindingAsync(id, r, ct)).Id };
    [HttpPost("reviews/{id:guid}/findings/{findingId:guid}/resolve")] public async Task<IActionResult> Resolve(Guid id, Guid findingId, CancellationToken ct) { await svc.ResolveFindingAsync(id, findingId, ct); return NoContent(); }
    [HttpPost("reviews/{id:guid}/actions")] public async Task<object> Action(Guid id, CreateActionRequest r, CancellationToken ct) { var a = await svc.AddActionAsync(id, r, ct); return new { a.Id, a.Number }; }
    [HttpPost("reviews/{id:guid}/hold")] public async Task<IActionResult> Hold(Guid id, HoldReviewInput r, CancellationToken ct) { await svc.HoldAsync(id, r, ct); return NoContent(); }
    [HttpPost("reviews/{id:guid}/submit")] public async Task<IActionResult> Submit(Guid id, CancellationToken ct) { await svc.SubmitReviewAsync(id, ct); return NoContent(); }

    [HttpGet("products/{productId:guid}/variants")] public Task<List<ProductVariant>> Variants(Guid productId, CancellationToken ct) => svc.VariantsAsync(productId, ct);
    [HttpPost("products/{productId:guid}/variants")] public async Task<object> AddVariant(Guid productId, VariantInput r, CancellationToken ct) => new { (await svc.SaveVariantAsync(productId, null, r, ct)).Id };
    [HttpPut("products/{productId:guid}/variants/{id:guid}")] public async Task<IActionResult> UpdateVariant(Guid productId, Guid id, VariantInput r, CancellationToken ct) { await svc.SaveVariantAsync(productId, id, r, ct); return NoContent(); }

    [HttpGet("parts/{partId:guid}/bom")] public Task<object> Bom(Guid partId, [FromQuery] BomType type, CancellationToken ct) => svc.BomAsync(partId, type, ct);
    [HttpPut("parts/{partId:guid}/bom")] public async Task<IActionResult> SaveBom(Guid partId, [FromQuery] BomType type, List<BomLineInput> r, CancellationToken ct) { await svc.SaveBomAsync(partId, type, r, ct); return NoContent(); }
    [HttpGet("parts/{partId:guid}/bom/history")] public Task<object> BomHistory(Guid partId, CancellationToken ct) => svc.BomHistoryAsync(partId, ct);
}

/// <summary>Enterprise intelligence: supplier risk, risk matrix, global quality score, factory and executive centers, KPI framework, data marts, benchmarking, digital twin, board reports.</summary>
[ApiController, Authorize, Route("api/qos")]
public sealed class QualityOsController(IntelligenceService svc) : ControllerBase
{
    [HttpGet("supplier-risk")] [RequirePermission(Permissions.SuppliersView)] public Task<List<SupplierRisk>> SupplierRisk([FromQuery] Guid? supplierId, CancellationToken ct) => svc.SupplierRiskAsync(supplierId, null, ct);
    [HttpGet("risk-matrix")] [RequirePermission(Permissions.RisksView)] public Task<object> RiskMatrix([FromQuery] Guid? siteId, CancellationToken ct) => svc.RiskMatrixAsync(siteId, ct);
    [HttpGet("quality-score")] [RequirePermission(Permissions.EnterpriseView)] public Task<object> Score(CancellationToken ct) => svc.GlobalScoreAsync(ct);
    [HttpGet("factory/{siteId:guid}")] public Task<object> Factory(Guid siteId, CancellationToken ct) => svc.FactoryCenterAsync(siteId, ct);
    [HttpGet("command-center")] [RequirePermission(Permissions.EnterpriseView)] public Task<object> Command(CancellationToken ct) => svc.CommandCenterAsync(ct);
    [HttpGet("kpis")] [RequirePermission(Permissions.ObjectivesView)] public Task<object> Kpis(CancellationToken ct) => svc.KpiFrameworkAsync(ct);
    [HttpGet("marts/{name}")] [RequirePermission(Permissions.AnalyticsView)] public Task<object> Mart(string name, CancellationToken ct) => svc.MartAsync(name, ct);
    [HttpGet("benchmarks/{kind}")] [RequirePermission(Permissions.EnterpriseView)] public Task<object> Benchmark(string kind, CancellationToken ct) => svc.BenchmarkAsync(kind, ct);
    [HttpPost("twin/process")] [RequirePermission(Permissions.SpcView)] public Task<object> TwinProcess(TwinProcessInput r, CancellationToken ct) => svc.SimulateProcessAsync(r, ct);
    [HttpPost("twin/product")] [RequirePermission(Permissions.ApqpView)] public Task<object> TwinProduct(TwinProductInput r, CancellationToken ct) => svc.SimulateProductRiskAsync(r, ct);
    [HttpPost("twin/supplier")] [RequirePermission(Permissions.SuppliersView)] public Task<object> TwinSupplier(TwinSupplierInput r, CancellationToken ct) => svc.SimulateSupplierAsync(r, ct);
    [HttpGet("board/{kind}")] [RequirePermission(Permissions.EnterpriseView)] public Task<object> Board(string kind, CancellationToken ct) => svc.BoardReportAsync(kind, ct);
}

/// <summary>Knowledge base, root-cause library, playbooks, business processes and maturity assessments.</summary>
[ApiController, Authorize, Route("api/knowledge")]
public sealed class KnowledgeController(KnowledgeService svc) : ControllerBase
{
    [HttpGet] [RequirePermission(Permissions.KnowledgeView)] public Task<List<KnowledgeArticle>> List([FromQuery] string? q, [FromQuery] KnowledgeKind? kind, CancellationToken ct) => svc.ListAsync(q, kind, ct);
    [HttpPost] [RequirePermission(Permissions.KnowledgeEdit)] public async Task<object> Create(KnowledgeInput r, CancellationToken ct) { var a = await svc.CreateAsync(r, null, null, ct); return new { a.Id, a.Number }; }
    [HttpGet("{id:guid}")] public Task<object> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPut("{id:guid}")] public async Task<IActionResult> Update(Guid id, KnowledgeInput r, CancellationToken ct) { await svc.UpdateAsync(id, r, ct); return NoContent(); }
    [HttpPost("{id:guid}/publish")] public async Task<IActionResult> Publish(Guid id, PublishRequest r, CancellationToken ct) { await svc.PublishAsync(id, r.Published, ct); return NoContent(); }
    [HttpPost("from-capa/{capaId:guid}")] [RequirePermission(Permissions.KnowledgeEdit)] public async Task<object> FromCapa(Guid capaId, CancellationToken ct) { var a = await svc.CaptureFromCapaAsync(capaId, ct); return new { a.Id, a.Number }; }
    [HttpPost("similar")] [RequirePermission(Permissions.KnowledgeView)] public Task<List<KnowledgeHit>> Similar(TextRequest r, CancellationToken ct) => svc.SimilarAsync(r.Text, ct);

    [HttpGet("playbooks")] [RequirePermission(Permissions.KnowledgeView)] public Task<List<Playbook>> Playbooks([FromQuery] PlaybookTrigger? trigger, CancellationToken ct) => svc.PlaybooksAsync(trigger, ct);
    [HttpPost("playbooks")] [RequirePermission(Permissions.KnowledgeEdit)] public async Task<object> CreatePlaybook(PlaybookInput r, CancellationToken ct) { var p = await svc.CreatePlaybookAsync(r, ct); return new { p.Id, p.Number }; }
    [HttpPut("playbooks/{id:guid}")] [RequirePermission(Permissions.KnowledgeEdit)] public async Task<IActionResult> UpdatePlaybook(Guid id, PlaybookInput r, [FromQuery] bool? publish, CancellationToken ct) { await svc.UpdatePlaybookAsync(id, r, publish, ct); return NoContent(); }
    [HttpPost("playbooks/{id:guid}/apply")] [RequirePermission(Permissions.KnowledgeView)] public async Task<object> Apply(Guid id, ApplyPlaybookInput r, CancellationToken ct) => new { Actions = await svc.ApplyPlaybookAsync(id, r, ct) };

    [HttpGet("~/api/processes")] [RequirePermission(Permissions.ProcessView)] public Task<object> Processes([FromQuery] Guid? siteId, CancellationToken ct) => svc.ProcessDashboardAsync(siteId, ct);
    [HttpPost("~/api/processes")] [RequirePermission(Permissions.ProcessEdit)] public async Task<object> CreateProcess(ProcessInput r, CancellationToken ct) => new { (await svc.CreateProcessAsync(r, ct)).Id };
    [HttpGet("~/api/processes/{id:guid}")] public Task<object> Process(Guid id, CancellationToken ct) => svc.ProcessAsync(id, ct);
    [HttpPost("~/api/processes/{id:guid}/kpis/{objectiveId:guid}")] public async Task<IActionResult> LinkKpi(Guid id, Guid objectiveId, CancellationToken ct) { await svc.LinkKpiAsync(id, objectiveId, ct); return NoContent(); }
    [HttpPost("~/api/processes/{id:guid}/reviews")] public async Task<IActionResult> ReviewProcess(Guid id, ProcessReviewInput r, CancellationToken ct) { await svc.ReviewProcessAsync(id, r, ct); return NoContent(); }
    [HttpGet("~/api/maturity")] [RequirePermission(Permissions.ProcessView)] public Task<object> Maturity([FromQuery] MaturitySubject? subject, CancellationToken ct) => svc.MaturityAsync(subject, ct);
    [HttpPost("~/api/maturity")] [RequirePermission(Permissions.ProcessEdit)] public async Task<object> Assess(MaturityInput r, CancellationToken ct) { var a = await svc.AssessAsync(r, ct); return new { a.Id, a.Number, a.Overall }; }
}

/// <summary>Security reviews, privileged access reviews, access certification and deployment history.</summary>
[ApiController, Authorize, Route("api/security-governance"), RequirePermission(Permissions.SecurityReview)]
public sealed class SecurityGovernanceController(SecurityGovernanceService svc) : ControllerBase
{
    [HttpGet("reviews")] public Task<List<AccessReview>> Reviews(CancellationToken ct) => svc.ListAsync(ct);
    [HttpPost("reviews")] public async Task<object> Create(AccessReviewInput r, CancellationToken ct) { var a = await svc.CreateAsync(r, ct); return new { a.Id, a.Number }; }
    [HttpGet("reviews/{id:guid}")] public Task<object> Get(Guid id, CancellationToken ct) => svc.GetAsync(id, ct);
    [HttpPost("reviews/{id:guid}/items/{itemId:guid}")] public async Task<IActionResult> Decide(Guid id, Guid itemId, AccessDecisionInput r, CancellationToken ct) { await svc.DecideAsync(id, itemId, r, ct); return NoContent(); }
    [HttpPost("reviews/{id:guid}/complete")] public async Task<IActionResult> Complete(Guid id, OptionalTextRequest r, CancellationToken ct) { await svc.CompleteAsync(id, r.Text, ct); return NoContent(); }
    [HttpGet("deployments")] public Task<List<DeploymentRelease>> Deployments(CancellationToken ct) => svc.DeploymentsAsync(ct);
    [HttpPost("deployments")] public async Task<object> Plan(DeploymentInput r, CancellationToken ct) { var d = await svc.PlanDeploymentAsync(r, ct); return new { d.Id, d.Number }; }
    [HttpPost("deployments/{id:guid}/status")] public async Task<IActionResult> Status(Guid id, DeploymentStatusRequest r, CancellationToken ct) { await svc.SetDeploymentStatusAsync(id, r.Status, r.Notes, ct); return NoContent(); }
}

/// <summary>Quality advisor: explanations, conversation, management review drafts (human approval), document and audit assistants.</summary>
[ApiController, Authorize, Route("api/advisor"), RequirePermission(Permissions.CopilotUse)]
public sealed class AdvisorController(AdvisorService svc) : ControllerBase
{
    [HttpGet("explain/{topic}")] public Task<AdvisorAnswer> Explain(string topic, [FromQuery] Guid? id, CancellationToken ct) => svc.ExplainAsync(topic, id, ct);
    [HttpPost("chat")] public Task<CopilotAnswer> Chat(ChatRequest r, CancellationToken ct) => svc.ChatAsync(r.Messages, ct);
    [HttpPost("management-reviews/{id:guid}/draft")] public async Task<object> Draft(Guid id, CancellationToken ct) => new { Draft = await svc.DraftManagementReviewAsync(id, ct) };
    [HttpPost("management-reviews/{id:guid}/approve-draft")] public async Task<IActionResult> ApproveDraft(Guid id, OptionalTextRequest r, CancellationToken ct) { await svc.ApproveManagementReviewDraftAsync(id, r.Text, ct); return NoContent(); }
    [HttpPost("documents")] public Task<DocumentAssistResult> Document(DocumentAssistInput r, CancellationToken ct) => svc.DocumentAsync(r, ct);
    [HttpGet("audit-prep/{siteId:guid}")] public Task<object> AuditPrep(Guid siteId, [FromQuery] string? process, CancellationToken ct) => svc.AuditPrepAsync(siteId, process, ct);
}
