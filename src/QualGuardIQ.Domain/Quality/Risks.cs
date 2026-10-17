using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

public enum RiskKind { Risk = 0, Opportunity = 1 }
public enum RiskStatus { Open = 0, Mitigating = 1, PendingAcceptance = 2, Monitoring = 3, Closed = 4, Cancelled = 5 }

/// <summary>
/// Organization-wide risk scoring configuration: rating scales, scoring formula and score bands.
/// Levels and bands are stored as JSON so administrators can change the matrix without code.
/// </summary>
public class RiskSettings : Entity, IAudited
{
    /// <summary>JSON array of {value,label} for probability (likelihood).</summary>
    public string ProbabilityLevels { get; set; } = "[]";
    public string SeverityLevels { get; set; } = "[]";
    public string DetectabilityLevels { get; set; } = "[]";
    public bool UseDetectability { get; set; }
    /// <summary>JSON array of {minScore,label,color,requiresAcceptance,escalate}, evaluated highest minScore first.</summary>
    public string Bands { get; set; } = "[]";
    /// <summary>JSON array of category names offered in the register.</summary>
    public string Categories { get; set; } = "[]";
    public string AuditLabel => "Risk settings";
}

/// <summary>Risk or improvement opportunity with initial and residual assessment, acceptance and monitoring.</summary>
public class RiskItem : Entity, IAudited, ISiteScoped
{
    public string Number { get; set; } = "";
    public RiskKind Kind { get; set; }
    public string Category { get; set; } = "";
    public string? Source { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Cause { get; set; }
    public string? Consequence { get; set; }
    public string? Process { get; set; }
    public Guid? ProductId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid OwnerId { get; set; }

    public int Probability { get; set; }
    public int Severity { get; set; }
    public int? Detectability { get; set; }
    public int InitialScore { get; set; }
    public string InitialBand { get; set; } = "";
    public string? ExistingControls { get; set; }
    public string? MitigationPlan { get; set; }

    public int? ResidualProbability { get; set; }
    public int? ResidualSeverity { get; set; }
    public int? ResidualDetectability { get; set; }
    public int? ResidualScore { get; set; }
    public string? ResidualBand { get; set; }
    public string? AcceptanceCriteria { get; set; }
    public Guid? AcceptedById { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }

    public DateOnly ReviewDueOn { get; set; }
    public int ReviewIntervalDays { get; set; } = 180;
    public DateOnly? LastReviewedOn { get; set; }
    public string? Monitoring { get; set; }
    public bool Escalated { get; set; }
    public RiskStatus Status { get; set; }
    public string? ClosureNotes { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string AuditLabel => $"risk {Number}";
}

/// <summary>Periodic review of a risk: re-rating snapshot and notes.</summary>
public class RiskReview : Entity, IAudited
{
    public Guid RiskId { get; set; }
    public DateOnly ReviewedOn { get; set; }
    public Guid ReviewedById { get; set; }
    public int Score { get; set; }
    public string Band { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool StillEffective { get; set; }
    public string AuditLabel => $"Risk review {ReviewedOn:yyyy-MM-dd}";
}
