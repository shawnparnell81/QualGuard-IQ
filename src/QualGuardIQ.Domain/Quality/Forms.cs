using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum FormRecordStatus { Draft = 0, Submitted = 1, Approved = 2, Rejected = 3 }

/// <summary>A completed controlled form. Bound to the exact template revision it was filled against.</summary>
public class FormRecord : Entity, IAudited, ISiteScoped, IContentLock
{
    public string Number { get; set; } = "";
    public Guid TemplateId { get; set; }
    public Guid TemplateRevisionId { get; set; }
    public string TemplateNumber { get; set; } = "";
    public string TemplateRevision { get; set; } = "";
    public string Title { get; set; } = "";
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public FormRecordStatus Status { get; set; }
    public string ValuesJson { get; set; } = "{}";
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? SubmittedById { get; set; }
    public string AuditLabel => $"form record {Number}";
    public string StatusProperty => nameof(Status);
    public bool IsLockedState(object? s) => s is FormRecordStatus.Submitted or FormRecordStatus.Approved;
    public IReadOnlyCollection<string> MutableWhenLocked { get; } = [nameof(Status)];
}
