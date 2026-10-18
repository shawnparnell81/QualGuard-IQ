namespace QualGuardIQ.Infrastructure.Modules.Imports.Excel;

/// <summary>One line of the fidelity report: what happened to a part of the source and why.</summary>
/// <param name="Area">layout, formula, validation, conditional, table, checkbox, signature, field, print, object, font, sheet, security</param>
/// <param name="Status">converted, approximated, preserved (kept in the original file only), not-converted</param>
public sealed record FidelityItem(string Area, string Location, string Status, string Detail);

public sealed record FormulaCounts(int Total, int Converted, int Preserved, int NotConverted);

/// <summary>Fidelity report for a conversion (FI 7, TECH 5/6 fidelity review): every element that was not converted exactly, with the reason.</summary>
public sealed class FidelityReport
{
    public string Source { get; set; } = "xlsx";
    public string ToolVersion { get; set; } = "";
    public string EvaluationVersion { get; set; } = "";
    public int RenderingVersion { get; set; } = 1;
    public List<FidelityItem> Items { get; set; } = [];
    public FormulaCounts Formulas { get; set; } = new(0, 0, 0, 0);
    public List<string> MissingFonts { get; set; } = [];
    public Dictionary<string, int> Counts { get; set; } = [];
    public int Fields { get; set; }
    public int Tables { get; set; }
    public int Signatures { get; set; }
    public int Rules { get; set; }
    public void Add(string area, string location, string status, string detail) => Items.Add(new FidelityItem(area, location, status, detail));
}

/// <summary>Where a source cell, content control or region went: a field, table column, total, signature or literal value.</summary>
public sealed record FieldMapEntry(string Source, string Target, string Kind, string? Label = null);
