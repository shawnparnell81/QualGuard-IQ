using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QualGuardIQ.Application.Records;

namespace QualGuardIQ.Api.Controllers;

/// <summary>Display labels for record types. Identifiers stay unchanged in the API and database; labels are display metadata only.</summary>
[ApiController]
[Authorize]
[Route("api/record-types")]
public sealed class RecordTypesController(IRecordRegistry records, RecordTypeLabels labels) : ControllerBase
{
    public sealed record RecordTypeDto(string Type, string Label);

    [HttpGet]
    public IEnumerable<RecordTypeDto> List() => records.KnownTypes.Select(t => new RecordTypeDto(t, labels.For(t))).OrderBy(t => t.Label);
}
