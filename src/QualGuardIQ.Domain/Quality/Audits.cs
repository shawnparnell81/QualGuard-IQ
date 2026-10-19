using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum AuditStatus { Planned = 0, InProgress = 1, Reporting = 2, Closed = 3, Cancelled = 4 }
public enum ChecklistResult { NotAssessed = 0, Conforming = 1, Nonconforming = 2, Observation = 3, NotApplicable = 4 }
public enum FindingType { MajorNonconformity = 0, MinorNonconformity = 1, Observation = 2, OpportunityForImprovement = 3 }
public enum FindingStatus { Open = 0, ActionPlanned = 1, Verified = 2, Closed = 3 }

public class Audit : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string AuditType { get; set; } = "Internal";
    public string Scope { get; set; } = "";
    public string? Criteria { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid LeadAuditorId { get; set; }
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public AuditStatus Status { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public List<AuditChecklistItem> Checklist { get; set; } = [];
    public string AuditLabel => $"audit {Number}";
}

public class AuditChecklistItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AuditId { get; set; }
    public int Sequence { get; set; }
    public string? Clause { get; set; }
    public string Question { get; set; } = "";
    public ChecklistResult Result { get; set; }
    public string? Evidence { get; set; }
}

public class AuditFinding : Entity, IAudited, ISiteScoped, ISoftDeletable
{
    public string Number { get; set; } = "";
    public Guid AuditId { get; set; }
    public Guid? ChecklistItemId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public FindingType FindingType { get; set; }
    public string? Clause { get; set; }
    public string Statement { get; set; } = "";
    public string? Evidence { get; set; }
    public Guid ResponsibleId { get; set; }
    public DateOnly ResponseDueOn { get; set; }
    public FindingStatus Status { get; set; }
    public string? VerificationNotes { get; set; }
    public string AuditLabel => $"finding {Number}";
}
