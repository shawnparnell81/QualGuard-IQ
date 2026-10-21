namespace QualGuardIQ.Infrastructure.Records;

/// <summary>
/// Quality-event records. Once one of these is live (saved and past draft) it is part of the quality history: it can only be deleted by
/// someone holding the "delete live quality records" permission, otherwise it is closed out with a reason. Records linked to one of
/// these (an 8D or containment form on a CAPA, for example) follow the same rule.
/// </summary>
public static class CriticalRecords
{
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Ncr",                        // nonconformance reports, including their containment and disposition
        "Capa",                       // corrective and preventive actions, including 8D
        "Complaint",                  // customer complaints and returned-material evaluation
        "SupplierCorrectiveAction",   // SCARs
        "MrbCase",                    // material review board cases
        "CustomerReturn",             // RMAs
        "WarrantyClaim",
        "Recall",
        "QualityEvent",
        "Deviation",
        "AuditFinding",
        "ImpactAssessment",           // out-of-tolerance impact assessments
    };

    public static bool IsCritical(string type) => Types.Contains(type);
}
