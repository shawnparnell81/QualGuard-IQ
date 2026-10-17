using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Platform;

public enum LibraryStatus { Draft = 0, Published = 1, Retired = 2 }

/// <summary>Reusable form component in the form library (for example "Lot identification" or "Dimensional check"). Versioned; templates copy a published version so later changes never alter released forms.</summary>
public class FormComponent : Entity, IAudited
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    public int Version { get; set; } = 1;
    public LibraryStatus Status { get; set; }
    public string FieldsJson { get; set; } = "[]";
    public DateTimeOffset? PublishedAt { get; set; }
    public Guid? PublishedById { get; set; }
    public string AuditLabel => $"form component {Key} v{Version}";
}
