using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Organization;

/// <summary>
/// How the application looks: light, dark or system mode, the primary and accent colours, and the colour palette each form family uses.
/// One row with no user is the organization default (changed by holders of the organization settings permission); a row with a user is
/// that person's own override. Empty values fall back to the organization default, then to the built-in look.
/// </summary>
public class AppearanceSetting : Entity, IAudited
{
    /// <summary>Null for the organization default; otherwise the person this override belongs to.</summary>
    public Guid? UserId { get; set; }
    /// <summary>light, dark or system; null to inherit.</summary>
    public string? Mode { get; set; }
    public string? PrimaryColor { get; set; }
    public string? AccentColor { get; set; }
    /// <summary>JSON object: form family (report, worksheet, checklist, certificate, investigation, register, plan) → { palette, edge, accent }.</summary>
    public string FormPalettes { get; set; } = "{}";
    public string AuditLabel => UserId is null ? "Organization appearance" : "Personal appearance";
}
