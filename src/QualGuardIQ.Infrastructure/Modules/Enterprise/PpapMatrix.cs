using QualGuardIQ.Domain.Enterprise;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

/// <summary>One row of the PPAP retention/submission matrix. Codes holds the level 1-5 codes in order (S, R or *).</summary>
public sealed record PpapMatrixRow(string Key, int? ElementCode, string Name, string? Condition, string Codes)
{
    public string CodeFor(int level) => Codes[level - 1].ToString();
}

/// <summary>
/// PPAP submission level matrix: AIAG PPAP 4th Edition, Section 4, Table 4.2 "Retention/Submission Requirements".
/// Verified cell by cell against two independent published reproductions of the table:
///   - Quality-One International, "PPAP Submission Requirements per Level"
///     (https://quality-one.com/wp-content/uploads/2016/12/PPAP-Submission-Levels.pdf)
///   - Accurate Products, "Retention/Submission Requirements"
///     (https://www.accurate-prod.com/wp-content/uploads/2024/04/ppap-level-requirements.pdf)
/// Codes: S = submit to customer and retain; R = retain, available on request; * = retain, submit on request.
/// Level 4 is "*" for every element except the PSW (and bulk material checklist), whose content the customer defines.
/// Level 5 is reviewed at the organization's site, so every row is R there, including the PSW.
///
///   Element                                        L1 L2 L3 L4 L5
///   1  Design record - proprietary details          R  R  R  *  R
///   1  Design record - all other details            R  S  S  *  R
///   2  Engineering change documents (if any)        R  S  S  *  R
///   3  Customer engineering approval (if required)  R  R  S  *  R
///   4  Design FMEA                                  R  R  S  *  R
///   5  Process flow diagrams                        R  R  S  *  R
///   6  Process FMEA                                 R  R  S  *  R
///   7  Control plan                                 R  R  S  *  R
///   8  MSA studies                                  R  R  S  *  R
///   9  Dimensional results                          R  S  S  *  R
///   10 Material, performance test results           R  S  S  *  R
///   11 Initial process studies                      R  R  S  *  R
///   12 Qualified laboratory documentation           R  S  S  *  R
///   13 Appearance approval report (if applicable)   S  S  S  *  R
///   14 Sample production parts                      R  S  S  *  R
///   15 Master sample                                R  R  R  *  R
///   16 Checking aids                                R  R  R  *  R
///   17 Records of compliance with CSRs              R  R  S  *  R
///   18 Part submission warrant (PSW)                S  S  S  S  R
///   -  Bulk material requirements checklist         S  S  S  S  R   (bulk material PPAP only)
/// </summary>
public static class PpapMatrix
{
    public static readonly PpapMatrixRow[] Rows =
    [
        new("1a", null, "Design records: proprietary components/details", null, "RRR*R"),
        new("1", 1, "Design records: all other components/details", null, "RSS*R"),
        new("2", 2, "Engineering change documents", "If any", "RSS*R"),
        new("3", 3, "Customer engineering approval", "If required", "RRS*R"),
        new("4", 4, "Design FMEA", null, "RRS*R"),
        new("5", 5, "Process flow diagrams", null, "RRS*R"),
        new("6", 6, "Process FMEA", null, "RRS*R"),
        new("7", 7, "Control plan", null, "RRS*R"),
        new("8", 8, "Measurement system analysis studies", null, "RRS*R"),
        new("9", 9, "Dimensional results", null, "RSS*R"),
        new("10", 10, "Material / performance test results", null, "RSS*R"),
        new("11", 11, "Initial process studies", null, "RRS*R"),
        new("12", 12, "Qualified laboratory documentation", null, "RSS*R"),
        new("13", 13, "Appearance approval report", "If applicable", "SSS*R"),
        new("14", 14, "Sample production parts", null, "RSS*R"),
        new("15", 15, "Master sample", null, "RRR*R"),
        new("16", 16, "Checking aids", null, "RRR*R"),
        new("17", 17, "Records of compliance with customer-specific requirements", null, "RRS*R"),
        new("18", 18, "Part submission warrant (PSW)", null, "SSSSR"),
        new("BMC", null, "Bulk material requirements checklist", "Bulk material PPAP only", "SSSSR"),
    ];

    public static readonly (string Code, string Meaning)[] Legend =
    [
        ("S", "Submit to the customer and retain a copy"),
        ("R", "Retain and make available to the customer on request"),
        ("*", "Retain and submit to the customer on request"),
    ];

    public static string CodeOf(PpapRequirement r) => r switch { PpapRequirement.Submit => "S", PpapRequirement.Retain => "R", PpapRequirement.SubmitOnRequest => "*", _ => "" };

    public static PpapRequirement FromCode(char c) => c switch { 'S' => PpapRequirement.Submit, 'R' => PpapRequirement.Retain, '*' => PpapRequirement.SubmitOnRequest, _ => PpapRequirement.None };

    /// <summary>Requirement for a tracked element (1-18) at a level (1-5).</summary>
    public static PpapRequirement RequirementFor(int level, int code)
    {
        if (level is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(level));
        var row = Rows.FirstOrDefault(r => r.ElementCode == code) ?? throw new ArgumentOutOfRangeException(nameof(code));
        return FromCode(row.Codes[level - 1]);
    }
}
