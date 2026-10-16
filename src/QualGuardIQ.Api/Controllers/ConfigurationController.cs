using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Administrator configuration: approval routes, custom fields, numbering.</summary>
[ApiController]
[Route("api/admin/config")]
[RequirePermission(Permissions.AdminOrganization)]
public sealed class ConfigurationController(AppDbContext db, IRecordRegistry records) : ControllerBase
{
    public sealed record StepDto(int Order, string Name, string RequiredPermission, string SignatureMeaning);
    public sealed record RouteDto(Guid Id, string RecordType, string Name, int Version, bool PreventSelfApproval, IReadOnlyList<StepDto> Steps);
    public sealed record SaveRouteRequest(string Name, bool PreventSelfApproval, IReadOnlyList<StepDto> Steps);
    public sealed record SaveFieldRequest(string RecordType, string Key, string Label, CustomFieldType FieldType, bool IsRequired, List<string>? Options, int SortOrder);

    [HttpGet("approval-routes")]
    public async Task<IEnumerable<RouteDto>> Routes(CancellationToken ct) =>
        (await db.ApprovalRoutes.AsNoTracking().Include(r => r.Steps).Where(r => r.IsActive).OrderBy(r => r.RecordType).ToListAsync(ct))
        .Select(r => new RouteDto(r.Id, r.RecordType, r.Name, r.Version, r.PreventSelfApproval, r.Steps.OrderBy(s => s.Order).Select(s => new StepDto(s.Order, s.Name, s.RequiredPermission, s.SignatureMeaning)).ToList()));

    /// <summary>Replaces a route's steps. Requests already in progress keep the step numbers they started with.</summary>
    [HttpPut("approval-routes/{recordType}")]
    public async Task<IActionResult> SaveRoute(string recordType, SaveRouteRequest req, CancellationToken ct)
    {
        if (!records.IsKnownType(recordType)) throw new NotFoundException($"Record type '{recordType}'");
        if (req.Steps.Count is 0 or > 10) throw new AppException("A route needs 1 to 10 steps.");
        if (req.Steps.Select(s => s.Order).Distinct().Count() != req.Steps.Count) throw new AppException("Step order numbers must be unique.");
        foreach (var s in req.Steps)
        {
            if (!Permissions.All.Contains(s.RequiredPermission)) throw new AppException($"Unknown permission '{s.RequiredPermission}'.");
            if (string.IsNullOrWhiteSpace(s.Name) || string.IsNullOrWhiteSpace(s.SignatureMeaning)) throw new AppException("Each step needs a name and a signature meaning.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var current = await db.ApprovalRoutes.FirstOrDefaultAsync(r => r.RecordType == recordType && r.IsActive, ct);
        var version = 1;
        if (current is not null)
        {
            current.IsActive = false;
            version = await db.ApprovalRoutes.Where(r => r.RecordType == recordType).MaxAsync(r => r.Version, ct) + 1;
            await db.SaveChangesAsync(ct);
        }
        var route = new ApprovalRoute { RecordType = recordType, Name = req.Name.Trim(), PreventSelfApproval = req.PreventSelfApproval, Version = version };
        route.Steps = req.Steps.Select(s => new ApprovalRouteStep { RouteId = route.Id, Order = s.Order, Name = s.Name.Trim(), RequiredPermission = s.RequiredPermission, SignatureMeaning = s.SignatureMeaning.Trim() }).ToList();
        db.ApprovalRoutes.Add(route);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
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
