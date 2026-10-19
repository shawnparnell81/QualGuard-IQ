using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Organization;

/// <summary>A person's own settings. A missing row means every default applies.</summary>
public class UserPreference : Entity, IAudited
{
    public Guid UserId { get; set; }
    /// <summary>Send email copies of assignments and reminders (in-app notifications always appear).</summary>
    public bool EmailNotifications { get; set; } = true;
    /// <summary>Date display format; null follows the organization setting.</summary>
    public string? DateFormat { get; set; }
    /// <summary>"light", "dark" or "system"; null follows the device.</summary>
    public string? Theme { get; set; }
    public string AuditLabel => $"settings for {AuditRef.To(UserId, "a person")}";
}

/// <summary>Date formats people can choose. "auto" follows the browser's language.</summary>
public static class DateFormats
{
    public static readonly IReadOnlyList<string> All = ["auto", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "dd-MMM-yyyy", "dd.MM.yyyy"];
}
