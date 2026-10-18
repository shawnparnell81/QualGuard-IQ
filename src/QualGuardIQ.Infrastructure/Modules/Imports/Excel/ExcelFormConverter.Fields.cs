using System.Globalization;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Excel;

/// <summary>Worksheet scan: checkboxes, tables, input fields, calculated fields and signature blocks.</summary>
public sealed partial class ExcelFormConverter
{
    void ConvertSheet(XlSheet sh)
    {
        var reg = regions[sh.Name];
        var section = new FormSection { Key = NewKey(sh.Name, "sheet"), Title = sh.Name.Length > 200 ? sh.Name[..200] : sh.Name, Columns = 2, Orientation = sh.Orientation, PageBreakBefore = def.Sections.Count > 0 };
        def.Sections.Add(section);

        DetectCheckboxes(sh, reg, section);
        foreach (var t in sh.Tables.Where(t => t.R1 >= reg.R1 && t.R2 <= reg.R2 && t.C1 >= reg.C1 && t.C2 <= reg.C2)) ListObjectTable(sh, t, section);
        DetectBands(sh, reg, section);

        var positions = sh.Cells.Keys.Where(k => k.R >= reg.R1 && k.R <= reg.R2 && k.C >= reg.C1 && k.C <= reg.C2).ToHashSet();
        // blank styled cells are stored as cells too; merged areas and validated ranges may have no stored cell
        foreach (var m in sh.Merges.Where(m => m.R1 >= reg.R1 && m.R1 <= reg.R2 && m.C1 >= reg.C1 && m.C1 <= reg.C2)) positions.Add((m.R1, m.C1));
        foreach (var v in sh.Validations)
            for (var r = Math.Max(v.R1, reg.R1); r <= Math.Min(v.R2, reg.R2) && r - v.R1 < 2000; r++)
                for (var c = Math.Max(v.C1, reg.C1); c <= Math.Min(v.C2, reg.C2); c++) positions.Add((r, c));
        foreach (var p in precedents.Where(p => p.Item1 == SK(sh.Name) && p.Item2 >= reg.R1 && p.Item2 <= reg.R2 && p.Item3 >= reg.C1 && p.Item3 <= reg.C2)) positions.Add((p.Item2, p.Item3));

        SignatureBlock? lastSig = null; var lastSigRow = -10;
        foreach (var (r, c) in positions.OrderBy(k => k.R).ThenBy(k => k.C))
        {
            if (Covered(sh, r, c) || BindAt(sh.Name, r, c) is not null) continue;
            var cell = sh.At(r, c);
            if (cell?.Formula is not null) { Calc(sh, r, c, cell, section.Key, reg); continue; }
            if (!IsInput(sh, r, c, cell, reg)) continue;
            var label = LabelFor(sh, r, c, reg);
            var addr = FormulaRefs.Address(sh.Name, r, c);
            if (label is not null && SigRx.IsMatch(label) && !DateLabelRx.IsMatch(label) && !Precedent(sh, r, c))
            {
                var stem = Regex.Replace(label, "signature", "", RegexOptions.IgnoreCase).Trim();
                var sig = new SignatureBlock { Key = NewKey(stem.Length > 1 ? stem + "_signature" : "signature", "signature"), Label = label, Section = section.Key, Order = def.Signatures.Count + 1 };
                (sig.Role, sig.Meaning) = SignatureRole(label);
                def.Signatures.Add(sig);
                SetBind(sh.Name, r, c, new Bind("sig-name", sig.Key));
                map.Add(new FieldMapEntry(addr, $"{sig.Key} (signer)", "signature", label));
                report.Add("signature", addr, "converted", $"'{label}' became a signature block ({sig.Role}); signing asks for the password and authenticator code.");
                lastSig = sig; lastSigRow = r;
                continue;
            }
            if (label is not null && DateLabelRx.IsMatch(label) && lastSig is not null && r - lastSigRow <= 1 && !Precedent(sh, r, c) && !binds.Values.Any(b => b.Kind == "sig-date" && b.Key == lastSig.Key))
            {
                SetBind(sh.Name, r, c, new Bind("sig-date", lastSig.Key));
                map.Add(new FieldMapEntry(addr, $"{lastSig.Key} (date)", "signature", label));
                report.Add("signature", addr, "converted", $"'{label}' is filled with the date and time of the '{lastSig.Label}' signature.");
                continue;
            }
            Input(sh, r, c, cell, label, section.Key, addr);
        }
    }

    static (string Role, string Meaning) SignatureRole(string label)
    {
        var l = label.ToLowerInvariant();
        if (l.Contains("witness")) return ("witness", "I witnessed this work");
        if (l.Contains("prepared") || l.Contains("originator") || l.Contains("author")) return ("prepared", "I prepared this record");
        if (l.Contains("inspect") || l.Contains("perform") || l.Contains("operator") || l.Contains("technician") || l.Contains("completed")) return ("performed", "I performed this work as recorded");
        if (l.Contains("verif") || l.Contains("checked")) return ("verified", "I verified this record");
        if (l.Contains("review")) return ("reviewed", "I reviewed this record");
        if (l.Contains("customer")) return ("customer", "Customer acceptance");
        if (l.Contains("supplier")) return ("supplier", "Supplier acknowledgement");
        return ("approved", "I approve this record");
    }

    void Input(XlSheet sh, int r, int c, XlCell? cell, string? label, string section, string addr)
    {
        var name = SingleCellName(sh, r, c);
        var f = new FormField { Label = label ?? name ?? FormulaRefs.Address(r, c), Section = section, SourceLocation = addr };
        f.Key = NewKey(name ?? label, $"{sh.Name}_{FormulaRefs.Address(r, c)}");
        Type(sh, r, c, cell, f, label);
        if (cell?.Comment is { Length: > 0 } note) { f.Help = note.Length > 500 ? note[..500] : note; report.Add("field", addr, "converted", "Cell note became the field's help text."); }
        var span = Span(sh, r, c);
        f.Width = Math.Clamp((int)Math.Round(span.Cols * 3.0), 3, 12);
        if (f.Type == "text" && (span.Rows >= 2 || (cell?.Style?.Wrap == true && span.Cols >= 3))) f.Type = "longtext";
        if (sh.Protected && cell is { Locked: false } && !Blank(cell)) f.Placeholder = cell.Text;
        def.Fields.Add(f);
        SetBind(sh.Name, r, c, new Bind("field", f.Key));
        map.Add(new FieldMapEntry(addr, f.Key, "field", f.Label));
        if (name is not null) report.Add("field", addr, "converted", $"Named range {name} became field '{f.Key}'.");
    }

    string? SingleCellName(XlSheet sh, int r, int c)
    {
        foreach (var n in wb.Names.Where(n => n.Visible && (n.Scope is null || n.Scope == sh.Name)))
            if (FormulaRefs.Range(n.RefersTo) is { } x && x.R1 == x.R2 && x.C1 == x.C2 && x.R1 == r && x.C1 == c && string.Equals(x.Sheet ?? sh.Name, sh.Name, StringComparison.OrdinalIgnoreCase)) return n.Name;
        return null;
    }

    /// <summary>Field type from data validation, number format and label wording. Validation lists become dropdowns.</summary>
    void Type(XlSheet sh, int r, int c, XlCell? cell, FormField f, string? label)
    {
        var v = ValidationAt(sh, r, c);
        var nf = cell?.NumberFormat ?? cell?.Style?.NumberFormat;
        var addr = FormulaRefs.Address(sh.Name, r, c);
        if (v is not null)
        {
            switch (v.Type)
            {
                case "list":
                    var options = v.Options ?? ListValues(sh, v.ListSource);
                    if (options is { Count: > 0 })
                    {
                        options = options.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Length > 200 ? o[..200] : o).Distinct().Take(200).ToList();
                        if (options.Count == 2 && options.All(o => o.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || o.Equals("FALSE", StringComparison.OrdinalIgnoreCase))) { f.Type = "boolean"; report.Add("validation", addr, "converted", "TRUE/FALSE list became a checkbox."); return; }
                        if (options.Count <= 3 && options.All(IsGlyph)) { f.Type = "boolean"; report.Add("checkbox", addr, "converted", "Checkbox symbol list became a checkbox."); return; }
                        f.Type = "choice"; f.Options = options;
                        report.Add("validation", addr, "converted", $"Validation list became a dropdown with {options.Count} options{(v.ListSource is null ? "" : $" from {v.ListSource}")}.");
                        return;
                    }
                    report.Add("validation", addr, "not-converted", $"The validation list source '{v.ListSource}' could not be read; the field is plain text.");
                    break;
                case "whole" or "decimal":
                    f.Type = v.Type == "whole" ? "integer" : "number";
                    if (v.Operator is "Between" or "EqualOrGreaterThan" or "GreaterThan" && Num(v.Value) is { } mn) f.Min = mn;
                    if (v.Operator is "Between" && Num(v.Value2) is { } mx) f.Max = mx;
                    if (v.Operator is "EqualOrLessThan" or "LessThan" && Num(v.Value) is { } mx2) f.Max = mx2;
                    if (f.Min is { } a && f.Max is { } b && a > b) (f.Min, f.Max) = (b, a);
                    var exact = v.Operator is "Between" or "EqualOrGreaterThan" or "EqualOrLessThan";
                    report.Add("validation", addr, exact ? "converted" : "approximated",
                        $"Number validation became a {(f.Type == "integer" ? "whole number" : "number")} field{(f.Min is not null ? $" min {f.Min}" : "")}{(f.Max is not null ? $" max {f.Max}" : "")}{(exact ? "" : $" (Excel operator {v.Operator} is checked as inclusive or not at all)")}.");
                    if (WorkbookReader.Decimals(nf) is { } d && f.Type == "number") f.Decimals = d;
                    return;
                case "date": f.Type = "date"; report.Add("validation", addr, "converted", "Date validation became a date field."); return;
                case "time": f.Type = "time"; report.Add("validation", addr, "converted", "Time validation became a time field."); return;
                case "textLength":
                    f.Type = "text";
                    if (Num(v.Operator == "Between" ? v.Value2 : v.Value) is { } ml && ml > 0 && ml <= 10000) f.MaxLength = (int)ml;
                    report.Add("validation", addr, "converted", $"Text length validation became a maximum of {f.MaxLength} characters."); return;
                case "custom":
                    report.Add("validation", addr, "not-converted", $"Custom validation formula '{v.Value}' is not converted; check the field manually.");
                    break;
            }
        }
        if (WorkbookReader.IsDateFormat(nf) || cell?.Kind == "date") { f.Type = "date"; return; }
        if (label is not null && DateLabelRx.IsMatch(label) && !arithmetic.Contains((SK(sh.Name), r, c))) { f.Type = "date"; return; }
        if (WorkbookReader.IsPercentFormat(nf)) { f.Type = "percent"; return; }
        if (WorkbookReader.IsCurrencyFormat(nf)) { f.Type = "currency"; f.Decimals = WorkbookReader.Decimals(nf) ?? 2; return; }
        if (cell?.Kind == "bool") { f.Type = "boolean"; return; }
        if (cell?.Kind == "number" || (nf is not null && Regex.IsMatch(nf, @"^[#,0\.]+$")) || arithmetic.Contains((SK(sh.Name), r, c)))
        {
            f.Type = "number";
            if (WorkbookReader.Decimals(nf) is { } d2) f.Decimals = d2;
            return;
        }
        f.Type = "text";
    }

    static decimal? Num(string? s) => decimal.TryParse(s?.TrimStart('='), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    List<string>? ListValues(XlSheet sh, string? source)
    {
        if (source is null) return null;
        var refText = source.TrimStart('=');
        var name = wb.Names.FirstOrDefault(n => string.Equals(n.Name, refText, StringComparison.OrdinalIgnoreCase));
        if (name is not null) refText = name.RefersTo;
        if (FormulaRefs.Range(refText) is not { } rg) return null;
        var src = rg.Sheet is null ? sh : wb.Sheet(rg.Sheet);
        if (src is null) return null;
        var list = new List<string>();
        for (var r = rg.R1; r <= rg.R2 && list.Count < 500; r++)
            for (var c = rg.C1; c <= rg.C2; c++)
                if (src.At(r, c) is { } x && !Blank(x)) list.Add((x.Formula is not null ? x.Cached : x.Text) ?? "");
        return list;
    }

    void DetectCheckboxes(XlSheet sh, (int R1, int C1, int R2, int C2) reg, FormSection section)
    {
        foreach (var cb in sh.Checkboxes)
        {
            if (cb.R is not { } r || cb.C is not { } c || r < reg.R1 || r > reg.R2 || c < reg.C1 || c > reg.C2 || BindAt(sh.Name, r, c) is not null)
            { report.Add("checkbox", sh.Name, "not-converted", "A checkbox control without a linked cell inside the converted region was not converted."); continue; }
            var label = LabelFor(sh, r, c, reg) ?? RightLabel(sh, r, c) ?? $"Checkbox {FormulaRefs.Address(r, c)}";
            AddCheck(sh, r, c, label, section.Key, "Checkbox control linked to the cell became a checkbox field.", null);
        }
        foreach (var cell in sh.Cells.Values.Where(x => x.Kind == "text" && x.Formula is null && x.R >= reg.R1 && x.R <= reg.R2 && x.C >= reg.C1 && x.C <= reg.C2).OrderBy(x => x.R).ThenBy(x => x.C).ToList())
        {
            if (BindAt(sh.Name, cell.R, cell.C) is not null) continue;
            var m = Regex.Match(cell.Text!.Trim(), @"^(☐|☑|☒|□|■|\[ \]|\[[xX]\])\s*(.*)$", RegexOptions.Singleline);
            if (!m.Success) continue;
            var rest = m.Groups[2].Value.Trim();
            var label = rest.Length > 0 ? rest : RightLabel(sh, cell.R, cell.C) ?? LabelFor(sh, cell.R, cell.C, reg) ?? $"Checkbox {FormulaRefs.Address(cell.R, cell.C)}";
            AddCheck(sh, cell.R, cell.C, label, section.Key, "Checkbox symbol became a checkbox field.", rest.Length > 0 ? rest : null);
        }
    }

    string? RightLabel(XlSheet sh, int r, int c)
    {
        var x = sh.At(r, c + Span(sh, r, c).Cols);
        return IsLabel(x) ? Clean(x!.Text!) : null;
    }

    void AddCheck(XlSheet sh, int r, int c, string label, string section, string why, string? inlineText)
    {
        var addr = FormulaRefs.Address(sh.Name, r, c);
        var f = new FormField { Key = NewKey(label, $"check_{FormulaRefs.Address(r, c)}"), Label = Clean(label), Type = "boolean", Section = section, SourceLocation = addr, Width = 4 };
        def.Fields.Add(f);
        SetBind(sh.Name, r, c, new Bind("check", f.Key, Column: inlineText));
        map.Add(new FieldMapEntry(addr, f.Key, "checkbox", f.Label));
        report.Add("checkbox", addr, "converted", why);
    }

    void Calc(XlSheet sh, int r, int c, XlCell cell, string section, (int R1, int C1, int R2, int C2) reg, bool hidden = false)
    {
        var label = hidden ? null : LabelFor(sh, r, c, reg);
        var name = SingleCellName(sh, r, c);
        var f = new FormField { Key = NewKey(name ?? label, $"{sh.Name}_{FormulaRefs.Address(r, c)}"), Label = label ?? name ?? $"{sh.Name} {FormulaRefs.Address(r, c)}", Type = "calc", Formula = "0", Section = section, SourceLocation = FormulaRefs.Address(sh.Name, r, c) };
        if (WorkbookReader.Decimals(cell.NumberFormat) is { } d) f.Decimals = Math.Min(d, 8);
        f.Width = Math.Clamp((int)Math.Round(Span(sh, r, c).Cols * 3.0), 3, 12);
        def.Fields.Add(f);
        SetBind(sh.Name, r, c, new Bind("calc", f.Key));
        pending.Add(new Pending(sh.Name, r, c, cell.Formula!, f, null, hidden));
    }
}
