using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum AssignmentStatus { Assigned = 0, Completed = 1, Superseded = 2, Cancelled = 3 }

public class TrainingCourse : Entity, IAudited, ISiteScoped, IArchivable
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    /// <summary>Controlled document the course trains on. A new released revision triggers retraining when RetrainOnRevision is set.</summary>
    public Guid? DocumentId { get; set; }
    public bool RetrainOnRevision { get; set; } = true;
    public int? ValidityMonths { get; set; }
    public int DueDays { get; set; } = 30;
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public string AuditLabel => $"course {Number}";
}

public class TrainingAssignment : Entity, IAudited, ISiteScoped
{
    public Guid CourseId { get; set; }
    public Guid UserId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? DocumentRevisionId { get; set; }
    public string? DocumentRevision { get; set; }
    public DateOnly DueOn { get; set; }
    public AssignmentStatus Status { get; set; }
    public string Reason { get; set; } = "Assigned";
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletionNotes { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string AuditLabel => $"training assignment {CourseId}/{UserId}";
}
