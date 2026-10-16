using System.Security.Claims;
using QualGuardIQ.Application.Abstractions;

namespace QualGuardIQ.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User;
    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string UserName => Principal?.Identity?.IsAuthenticated == true ? Principal.FindFirstValue("display_name") ?? Principal.Identity!.Name ?? "unknown" : "system";
    public string? IpAddress => http.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public string? CorrelationId => http.HttpContext?.TraceIdentifier;
}
