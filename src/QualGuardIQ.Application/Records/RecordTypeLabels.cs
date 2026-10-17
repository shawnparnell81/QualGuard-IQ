using System.Text;
using System.Text.RegularExpressions;

namespace QualGuardIQ.Application.Records;

/// <summary>
/// Display names for record types. The internal identifiers (ChangeRequest, Fai, ...) stay unchanged in the API and database;
/// screens and messages show these labels. Deployments can override or add labels under configuration "RecordTypeLabels:{Type}".
/// Unknown types fall back to the identifier split into words, with known quality acronyms upper-cased.
/// </summary>
public sealed partial class RecordTypeLabels
{
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ChangeRequest"] = "Change Request",
        ["Fai"] = "First Article Inspection",
        ["AqlInspection"] = "AQL Inspection",
        ["SupplierCorrectiveAction"] = "Supplier Corrective Action",
        ["ManagementReview"] = "Management Review",
        ["Ncr"] = "Nonconformance Report",
        ["Capa"] = "Corrective and Preventive Action",
        ["FormRecord"] = "Form Record",
        ["Ppap"] = "PPAP Submission",
        ["PpapSubmission"] = "PPAP Submission",
        ["Apqp"] = "APQP Project",
        ["ApqpProject"] = "APQP Project",
        ["Pfmea"] = "Process FMEA",
        ["Dfmea"] = "Design FMEA",
        ["Fmea"] = "FMEA",
    };

    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
    { "aql", "fai", "ncr", "capa", "ppap", "apqp", "fmea", "pfmea", "dfmea", "spc", "msa", "lpa", "scar", "rma", "mrb", "ecr", "ecn", "eco", "bol", "po", "erp", "mes", "coq", "sop", "kpi", "dcr", "id", "qr", "8d" };

    private readonly IReadOnlyDictionary<string, string> _overrides;

    public RecordTypeLabels(IReadOnlyDictionary<string, string>? overrides = null) =>
        _overrides = overrides is null ? new Dictionary<string, string>() : new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);

    /// <summary>The label for a record type; never throws and never returns an empty string.</summary>
    public string For(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "Record";
        if (_overrides.TryGetValue(type, out var o) && !string.IsNullOrWhiteSpace(o)) return o;
        if (Defaults.TryGetValue(type, out var d)) return d;
        return Humanize(type);
    }

    public static string Humanize(string type)
    {
        var words = Words().Matches(type).Select(m => m.Value).ToList();
        if (words.Count == 0) return type;
        var sb = new StringBuilder();
        foreach (var w in words)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Acronyms.Contains(w) ? w.ToUpperInvariant() : char.ToUpperInvariant(w[0]) + w[1..]);
        }
        return sb.ToString();
    }

    [GeneratedRegex("[A-Z]{2,}(?=[A-Z][a-z]|\\d|$)|[A-Z]?[a-z]+|[A-Z]+|\\d+[A-Za-z]?")]
    private static partial Regex Words();
}
