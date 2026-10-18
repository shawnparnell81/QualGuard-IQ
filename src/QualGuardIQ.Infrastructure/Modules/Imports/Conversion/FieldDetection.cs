using System.Text.RegularExpressions;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Conversion;

/// <summary>A suggested field type for a detected field, with the reason and how sure the suggestion is.</summary>
public sealed record TypeSuggestion(string Type, string Candidate, double Confidence, string Reason);

/// <summary>
/// Field detection (TECH Form conversion engine, field detection): proposes one of the candidate types for each detected field from its label,
/// the type the source already gave it and its options or formula. Suggestions are never applied on their own; the review decides.
/// </summary>
public static partial class FieldDetection
{
    /// <summary>The detection candidates and the form field type each becomes.</summary>
    public static readonly IReadOnlyList<(string Candidate, string Type)> Candidates =
    [
        ("Text", "text"), ("Long text", "longtext"), ("Integer", "integer"), ("Decimal", "number"), ("Currency", "currency"), ("Percentage", "percent"),
        ("Date", "date"), ("Date and time", "datetime"), ("Time", "time"), ("Checkbox", "boolean"), ("Radio button", "radio"), ("Single select", "choice"),
        ("Multiple select", "multichoice"), ("User", "user"), ("Department", "department"), ("Site", "site"), ("Product", "product"), ("Part", "part"),
        ("Supplier", "supplier"), ("Customer", "customer"), ("Equipment", "equipment"), ("Record relationship", "record"), ("Attachment", "attachment"),
        ("Image", "photo"), ("Signature", "signature"), ("Repeating row", "table"), ("Calculated field", "calc"),
    ];

    public static string CandidateOf(string type) => Candidates.FirstOrDefault(c => c.Type == type).Candidate ?? type switch
    {
        "passfail" => "Single select", "measurement" => "Decimal", "email" or "url" => "Text", "instruction" => "Text", _ => "Text",
    };

    // Ordered: the first matching rule wins, so the specific ones come first.
    static readonly (Regex Pattern, string Type, string Why)[] Rules =
    [
        (R(@"\bsignature\b|\bsigned\b|\bsign here\b|\binitials?\b"), "signature", "the label asks for a signature"),
        (R(@"\b(photo|photograph|picture|image|sketch)\b"), "photo", "the label asks for a picture"),
        (R(@"\b(attach(ment|ed)?|evidence|supporting doc(ument)?s?)\b"), "attachment", "the label asks for attached files"),
        (R(@"\bdate\s*(and|&|/)\s*time\b|\btime\s*stamp\b|\btimestamp\b"), "datetime", "the label names a date and time"),
        (R(@"\b(date|dated|due|expir(y|es|ation)|effective|issued on|received on)\b"), "date", "the label names a date"),
        (R(@"\b(time|start time|end time|clock)\b"), "time", "the label names a time of day"),
        (R(@"\b(ncr|capa|scar|8d|deviation|complaint|audit|ecr|ecn|change request|work order|po|purchase order)\s*(no\.?|number|#|ref(erence)?|id)\b"), "record", "the label refers to another record"),
        (R(@"\b(supplier|vendor|subcontractor)\b"), "supplier", "the label names a supplier"),
        (R(@"\b(customer|client)\b"), "customer", "the label names a customer"),
        (R(@"\b(equipment|gage|gauge|instrument|asset|tool)\s*(id|no\.?|number|#|name)?\b"), "equipment", "the label names equipment"),
        (R(@"\b(part\s*(no\.?|number|#|name)?|p/n|item\s*(no\.?|number))\b"), "part", "the label names a part"),
        (R(@"\bproduct\b"), "product", "the label names a product"),
        (R(@"\b(department|dept\.?)\b"), "department", "the label names a department"),
        (R(@"\b(site|plant|facility)\b"), "site", "the label names a site"),
        (R(@"\b(inspector|inspected by|prepared by|performed by|verified by|reviewed by|approved by|operator|owner|responsible|assigned to|auditor|requested by|reported by|originator|technician)\b"), "user", "the label names a person"),
        (R(@"\b(cost|price|amount|value \((usd|eur|gbp)\)|\$|usd|eur|gbp)\b|\$"), "currency", "the label names money"),
        (R(@"%|\b(percent(age)?|yield|rate)\b"), "percent", "the label names a percentage"),
        (R(@"\b(qty|quantity|count|number of|no\.? of|pcs|pieces|lot size|sample size)\b"), "integer", "the label names a count"),
        (R(@"\b(measurement|measured|reading|dimension|actual|nominal|tolerance|weight|length|width|height|diameter|thickness|temperature|pressure|torque)\b"), "number", "the label names a measured value"),
        (R(@"\b(description|comments?|remarks|notes|details|root cause|action taken|findings|observations?|justification|explanation)\b"), "longtext", "the label asks for a description"),
    ];

    static Regex R(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Suggests a type. <paramref name="currentType"/> is what the converter chose from the source; <paramref name="evidence"/> says whether the
    /// source proved it (a checkbox, a validation list, a formula or a number format) – then the source wins and the suggestion is the same type.
    /// </summary>
    public static TypeSuggestion Suggest(string label, string currentType, bool evidence, IReadOnlyList<string>? options = null)
    {
        if (currentType == "calc") return new("calc", "Calculated field", 0.95, "the source cell holds a formula");
        if (currentType == "boolean" && evidence) return new("boolean", "Checkbox", 0.95, "the source has a checkbox");
        if (currentType is "choice" or "radio" or "multichoice" && options is { Count: > 0 })
            return new(currentType, CandidateOf(currentType), evidence ? 0.95 : 0.8, evidence ? "the source has a list of allowed values" : "the label lists choices");
        var text = (label ?? "").Trim();
        if (text.Length == 0 || GenericLabel().IsMatch(text)) return new(currentType, CandidateOf(currentType), 0.3, "no label was found next to it");
        foreach (var (p, t, why) in Rules)
        {
            if (!p.IsMatch(text)) continue;
            if (t == currentType) return new(t, CandidateOf(t), evidence ? 0.95 : 0.85, why);
            // the source's own evidence (a date or number format) outranks a label guess between plain value types
            if (evidence && !IsReference(t) && t is not ("signature" or "photo" or "attachment")) return new(currentType, CandidateOf(currentType), 0.9, "the source cell format sets the type");
            return new(t, CandidateOf(t), 0.7, why);
        }
        return new(currentType, CandidateOf(currentType), evidence ? 0.9 : 0.75, evidence ? "the source cell format sets the type" : "a labelled blank; nothing more specific was recognized");
    }

    public static bool IsReference(string t) => QualGuardIQ.Application.Forms.FormDefinitions.ReferenceTypes.Contains(t);

    [GeneratedRegex(@"^(field|column|cell|blank|value|text|item)\s*\d*$", RegexOptions.IgnoreCase)]
    private static partial Regex GenericLabel();
}
