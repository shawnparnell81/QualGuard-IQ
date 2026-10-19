using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Documents;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Governed document type library. Anyone signed in can read it (to file documents); organization administrators add, rename and deactivate types.</summary>
[ApiController]
[Authorize]
[Route("api/document-types")]
public sealed class DocumentTypesController(DocumentTypeService types) : ControllerBase
{
    public sealed record DocumentTypeDto(Guid Id, DocumentKind Kind, string Name, string? Description, bool IsSystem, bool IsActive, int SortOrder, bool IsReference);
    static DocumentTypeDto Dto(DocumentTypeDefinition t) => new(t.Id, t.Kind, t.Name, t.Description, t.IsSystem, t.IsActive, t.SortOrder, t.IsReference);

    [HttpGet]
    public async Task<IEnumerable<DocumentTypeDto>> List([FromQuery] DocumentKind? kind, [FromQuery] bool includeInactive = false, CancellationToken ct = default) =>
        (await types.ListAsync(kind, includeInactive, ct)).Select(Dto);

    [HttpPost]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<DocumentTypeDto> Add(DocumentTypeInput r, CancellationToken ct) => Dto(await types.AddAsync(r, ct));

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<DocumentTypeDto> Update(Guid id, DocumentTypeUpdate r, CancellationToken ct) => Dto(await types.UpdateAsync(id, r, ct));

    /// <summary>Re-runs the idempotent seed (adds missing shipped types; never duplicates or undoes renames and deactivations).</summary>
    [HttpPost("seed")]
    [RequirePermission(Permissions.AdminOrganization)]
    public async Task<object> Seed(CancellationToken ct) => new { Added = await types.EnsureSeededAsync(ct) };
}
