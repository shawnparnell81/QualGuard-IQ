using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Administrator configuration: approval routes, custom fields, numbering.</summary>
[ApiController]
[Route("api/admin/config")]
[RequirePermission(Permissions.AdminOrganization)]
public sealed class ConfigurationController(AppDbContext db, IRecordRegistry records, RecordConditions conditions, WorkflowService workflow) : ControllerBase
{
    public sealed record StepDto(int Order, string Name, string RequiredPermission, string SignatureMeaning, Guid? RequiredSkillId = null, bool RequireMfa = false,
        int? Stage = null, string? ConditionField = null, string? ConditionOperator = null, string? ConditionValue = null, int? EscalateAfterHours = null, string? EscalationPermission = null);
    public sealed record RouteDto(Guid Id, string RecordType, string Name, int Version, bool PreventSelfApproval, IReadOnlyList<StepDto> Steps, Guid? SiteId = null);
    public sealed record SaveRouteRequest(string Name, bool PreventSelfApproval, IReadOnlyList<StepDto> Steps);
    public sealed record SaveFieldRequest(string RecordType, string Key, string Label, CustomFieldType FieldType, bool IsRequired, List<string>? Options, int SortOrder);

    [HttpGet("approval-routes")]
    public async Task<IEnumerable<RouteDto>> Routes(CancellationToken ct) =>
        (await db.ApprovalRoutes.AsNoTracking().Include(r => r.Steps).Where(r => r.IsActive).OrderBy(r => r.RecordType).ThenBy(r => r.SiteId).ToListAsync(ct))
        .Select(r => new RouteDto(r.Id, r.RecordType, r.Name, r.Version, r.PreventSelfApproval, r.Steps.OrderBy(s => s.Stage).ThenBy(s => s.Order).Select(s => new StepDto(s.Order, s.Name, s.RequiredPermission, s.SignatureMeaning, s.RequiredSkillId, s.RequireMfa, s.Stage, s.ConditionField, s.ConditionOperator, s.ConditionValue, s.EscalateAfterHours, s.EscalationPermission)).ToList(), r.SiteId));

    /// <summary>Replaces a route's steps. Requests already in progress keep the step numbers they started with.</summary>
    [HttpPut("approval-routes/{recordType}")]
    public async Task<IActionResult> SaveRoute(string recordType, SaveRouteRequest req, [FromQuery] Guid? siteId, CancellationToken ct)
    {
        if (siteId is { } sid && !await db.Sites.AnyAsync(x => x.Id == sid, ct)) throw new NotFoundException("Site");
        foreach (var s in req.Steps.Where(s => s.RequiredSkillId != null))
            if (!await db.Skills.AnyAsync(k => k.Id == s.RequiredSkillId, ct)) throw new AppException($"Step '{s.Name}' needs an existing skill or qualification.");
        if (!records.IsKnownType(recordType)) throw new NotFoundException($"Record type '{recordType}'");
        if (req.Steps.Count is 0 or > 20) throw new AppException("A route needs 1 to 20 steps.");
        if (req.Steps.Select(s => s.Order).Distinct().Count() != req.Steps.Count) throw new AppException("Step order numbers must be unique.");
        foreach (var s in req.Steps)
        {
            if (!Permissions.All.Contains(s.RequiredPermission)) throw new AppException($"Unknown permission '{s.RequiredPermission}'.");
            if (string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.SignatureMeaning)) throw new AppException("Each step needs a name and a signature meaning.");
            if (!string.IsNullOrWhiteSpace(s.ConditionField)) conditions.Validate(recordType, new Condition(s.ConditionField, s.ConditionOperator ?? "equals", s.ConditionValue));
            if (s.EscalateAfterHours is < 1 or > 2160) throw new AppException("Escalation time is 1 to 2160 hours.");
            if (s.EscalationPermission is { Length: > 0 } ep && !Permissions.All.Contains(ep)) throw new AppException($"Unknown permission '{ep}'.");
        }
        if (req.Steps.All(s => !string.IsNullOrWhiteSpace(s.ConditionField))) throw new AppException("At least one step must be unconditional, so every record has an approver.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var current = await db.ApprovalRoutes.FirstOrDefaultAsync(r => r.RecordType == recordType && r.SiteId == siteId && r.IsActive, ct);
        var version = 1;
        if (current is not null)
        {
            current.IsActive = false;
            version = await db.ApprovalRoutes.Where(r => r.RecordType == recordType && r.SiteId == siteId).MaxAsync(r => r.Version, ct) + 1;
            await db.SaveChangesAsync(ct);
        }
        var route = new ApprovalRoute { RecordType = recordType, Name = req.Name.Trim(), PreventSelfApproval = req.PreventSelfApproval, Version = version, SiteId = siteId };
        route.Steps = req.Steps.Select(s => new ApprovalRouteStep { RouteId = route.Id, Order = s.Order, Name = s.Name.Trim(), RequiredPermission = s.RequiredPermission, SignatureMeaning = s.SignatureMeaning.Trim(), RequiredSkillId = s.RequiredSkillId, RequireMfa = s.RequireMfa,
            Stage = s.Stage ?? s.Order, ConditionField = string.IsNullOrWhiteSpace(s.ConditionField) ? null : s.ConditionField.Trim(), ConditionOperator = string.IsNullOrWhiteSpace(s.ConditionField) ? null : (s.ConditionOperator ?? "equals"),
            ConditionValue = string.IsNullOrWhiteSpace(s.ConditionField) ? null : s.ConditionValue?.Trim(), EscalateAfterHours = s.EscalateAfterHours, EscalationPermission = string.IsNullOrWhiteSpace(s.EscalationPermission) ? null : s.EscalationPermission }).ToList();
        db.ApprovalRoutes.Add(route);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    public sealed record PreviewStep(int Stage, string Step, bool Applies);

    /// <summary>Designer palette: record types with routes, condition fields of a record type, operators and permissions.</summary>
    [HttpGet("workflow-designer/fields/{recordType}")]
    public IActionResult DesignerFields(string recordType)
    {
        if (!records.IsKnownType(recordType)) throw new NotFoundException($"Record type '{recordType}'");
        return Ok(new { fields = conditions.FieldsOf(recordType).Select(f => new { f.Name, f.Type, f.Options }), operators = RecordConditions.Operators, permissions = Permissions.All.Where(p => p.EndsWith(".approve") || p.EndsWith(".admin") || p.EndsWith(".edit")).Order() });
    }

    /// <summary>Shows which steps of the active route would apply to an existing record, stage by stage (dry run; nothing is submitted).</summary>
    [HttpGet("approval-routes/{recordType}/preview/{recordId:guid}")]
    public async Task<IEnumerable<PreviewStep>> Preview(string recordType, Guid recordId, [FromQuery] Guid? siteId, CancellationToken ct)
    {
        var route = await db.ApprovalRoutes.AsNoTracking().Include(r => r.Steps).Where(r => r.RecordType == recordType && r.IsActive && (r.SiteId == siteId || r.SiteId == null))
            .OrderBy(r => r.SiteId == null).FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Approval route");
        return (await workflow.PreviewAsync(route, recordType, recordId, ct)).Select(x => new PreviewStep(x.Stage, x.Step, x.Applies));
    }

    /// <summary>Removes a site-specific route; the site falls back to the shared corporate route. Requests in progress keep their route.</summary>
    [HttpDelete("approval-routes/{recordType}/sites/{siteId:guid}")]
    public async Task<IActionResult> RemoveSiteRoute(string recordType, Guid siteId, CancellationToken ct)
    {
        var current = await db.ApprovalRoutes.FirstOrDefaultAsync(r => r.RecordType == recordType && r.SiteId == siteId && r.IsActive, ct) ?? throw new NotFoundException("Site route");
        current.IsActive = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("custom-fields")]
    public async Task<IActionResult> Fields(CancellationToken ct) =>
        Ok(await db.CustomFieldDefinitions.AsNoTracking().OrderBy(f => f.RecordType).ThenBy(f => f.SortOrder).ToListAsync(ct));

    [HttpPost("custom-fields")]
    public async Task<ActionResult<Guid>> AddField(SaveFieldRequest req, CancellationToken ct)
    {
        if (!records.IsKnownType(req.RecordType)) throw new NotFoundException($"Record type '{req.RecordType}'");
        if (string.IsNullOrWhiteSpace(req.Key) || !System.Text.RegularExpressions.Regex.IsMatch(req.Key, "^[a-z][a-z0-9_]{0,49}$"))
            throw new AppException("Key must start with a letter and use lowercase letters, numbers and underscores.");
        if (string.IsNullOrWhiteSpace(req.Label) || req.Label.Length > 100) throw new AppException("A label of up to 100 characters is required.");
        if (req.FieldType == CustomFieldType.Choice && (req.Options is null || req.Options.Count == 0)) throw new AppException("Choice fields need options.");
        if (await db.CustomFieldDefinitions.AnyAsync(f => f.RecordType == req.RecordType && f.Key == req.Key, ct)) throw new ConflictException("That key already exists.");
        var f = new CustomFieldDefinition { RecordType = req.RecordType, Key = req.Key, Label = req.Label.Trim(), FieldType = req.FieldType, IsRequired = req.IsRequired, Options = req.Options ?? [], SortOrder = req.SortOrder };
        db.CustomFieldDefinitions.Add(f);
        await db.SaveChangesAsync(ct);
        return f.Id;
    }

    [HttpPost("custom-fields/{id:guid}/archive")]
    public async Task<IActionResult> ArchiveField(Guid id, CancellationToken ct)
    {
        var f = await db.CustomFieldDefinitions.FindAsync([id], ct) ?? throw new NotFoundException("Custom field");
        f.IsArchived = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
