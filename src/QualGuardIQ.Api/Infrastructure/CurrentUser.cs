using System.Security.Claims;
using QualGuardIQ.Application.Abstractions;

namespace QualGuardIQ.Api.Infrastructure;

/// <summary>The signed-in user; inside a scope that trusted server code gave a run-as identity (a scheduled report run), that identity.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor http, QualGuardIQ.Infrastructure.Services.RunAsContext runAs) : ICurrentUser
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User;
    public Guid? UserId => runAs.UserId is { } r ? r : Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string UserName => runAs.UserId is not null && runAs.UserName is { } rn ? rn : Principal?.Identity?.IsAuthenticated == true ? Principal.FindFirstValue("display_name") ?? Principal.Identity!.Name ?? "unknown" : "system";
    public string? IpAddress => http.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => http.HttpContext?.TraceIdentifier;
}
