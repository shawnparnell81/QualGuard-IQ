using System.Text.RegularExpressions;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;

namespace QualGuardIQ.Infrastructure.Modules.Imports.Conversion;

/// <summary>
/// One item of the conversion review: something the engine detected in the source (a field, table, column, formula, signature or approval
/// block, section, instruction or conditional area) with the type it was given, a suggestion, a confidence level and the review state.
/// Same shape for every source format, so one review screen serves Excel, Word and PDF.
/// </summary>
public sealed class ReviewItem
{
    public string Id { get; set; } = "";
    /// <summary>field, checkbox, column, table, formula, signature, approval, section, instruction, conditional</summary>
    public string Kind { get; set; } = "field";
    public string Label { get; set; } = "";
    public string Type { get; set; } = "text";
    public string? Suggested { get; set; }
    public string? SuggestedCandidate { get; set; }
    public string Candidate { get; set; } = "Text";
    /// <summary>Worksheet, page or document part plus the cell or paragraph.</summary>
    public string Location { get; set; } = "";
    /// <summary>1-based page or worksheet number, for page and worksheet confidence.</summary>
    public int Page { get; set; } = 1;
    public string? SectionId { get; set; }
    public double Confidence { get; set; }
    public string Level => ConversionEngine.Level(Confidence);
    /// <summary>cell, content-control, paragraph, table (where the converter found it).</summary>
    public string Source { get; set; } = "cell";
    public string Reason { get; set; } = "";
    public List<string>? Options { get; set; }
    public List<ReviewColumn>? Columns { get; set; }
    public string? Role { get; set; }
    public string? Formula { get; set; }
    public bool Include { get; set; } = true;
    public bool Confirmed { get; set; }
    public bool CanExclude { get; set; }
    public bool Typed { get; set; }
    /// <summary>True when this item must be confirmed before submission (low confidence or unmapped).</summary>
    public bool MustConfirm { get; set; }
}

public sealed record ReviewColumn(string Label, string Type, bool Total);

/// <summary>A conversion target family (NCR, CAPA, calibration, supplier, audit, APQP, PPAP): the elements a form of that kind normally carries.</summary>
public sealed record TargetProfile(string Key, string Name, string ModuleKey, IReadOnlyList<ProfileElement> Elements);
public sealed record ProfileElement(string Name, string Pattern, string Type, bool Signature = false);
public sealed record ProfileMatch(string Element, string? FieldKey, string? FieldLabel, string Type, bool Signature);

/// <summary>A confidence summary for a group of items: the weakest item sets the level so one doubtful item is never hidden.</summary>
public sealed record ConfidenceSummary(string Level, double Score, int High, int Medium, int Low, int Unmapped, int Items);

public sealed record EngineResult(FormDefinition Definition, List<ReviewItem> Items, object Confidence, object? Profile, int Required);

/// <summary>
/// Form conversion engine (TECH Form conversion engine, FI Form conversion engine): takes what a format converter produced (Excel, Word or PDF),
/// detects sections, labels, fields, tables, formulas, validations, signatures, approval blocks, instructions and conditional areas, suggests field
/// types, scores confidence at document, page, section, field, formula and table level, applies the person's corrections and checks the result
/// against the chosen form family. Nothing detected is published: low-confidence and unmapped items must be confirmed, and the converted form is
/// only a draft template after an approver accepts the fidelity report.
/// </summary>
public static class ConversionEngine
{
    public const double High = 0.85, Medium = 0.6;
    public static string Level(double c) => c >= High ? "high" : c >= Medium ? "medium" : c > 0 ? "low" : "unmapped";

    static readonly HashSet<string> Evidence = ["boolean", "choice", "radio", "multichoice", "calc", "date", "datetime", "time", "number", "integer", "currency", "percent", "passfail", "measurement"];
    static readonly HashSet<string> ApprovalRoles = ["reviewed", "approved", "customer", "supplier"];

    public static readonly IReadOnlyList<TargetProfile> Profiles =
    [
        new("ncr", "NCR form", "ncr",
        [
            new("Part", @"\bpart\b|p/n|item", "part"), new("Quantity affected", @"qty|quantity|pieces|pcs", "integer"),
            new("Nonconformance description", @"description|nonconform|defect|problem", "longtext"), new("Date found", @"date", "date"),
            new("Containment", @"contain", "longtext"), new("Disposition", @"disposition|use as is|rework|scrap", "choice"),
            new("Root cause", @"root cause|cause", "longtext"), new("Disposition approval", @"approv|review|disposition", "signature", true),
        ]),
        new("capa", "CAPA form", "capa",
        [
            new("Problem statement", @"problem|issue|description", "longtext"), new("Root cause", @"root cause|cause|why", "longtext"),
            new("Corrective action", @"corrective", "longtext"), new("Preventive action", @"preventive|prevent", "longtext"),
            new("Owner", @"owner|responsible|assigned", "user"), new("Due date", @"due|target date|completion date", "date"),
            new("Effectiveness verification", @"effective|verif", "longtext"), new("Closure approval", @"approv|clos", "signature", true),
        ]),
        new("calibration", "Calibration form", "calibration",
        [
            new("Equipment", @"equipment|gage|gauge|instrument|asset|serial", "equipment"), new("Calibration date", @"cal\w*\s*date|date", "date"),
            new("Next due date", @"next|due", "date"), new("Reference standard", @"standard|reference|master", "text"),
            new("As found / as left result", @"as found|as left|result|pass|fail|reading", "number"), new("Performed by", @"technician|calibrated by|performed|perform", "signature", true),
        ]),
        new("supplier", "Supplier form", "suppliers",
        [
            new("Supplier", @"supplier|vendor", "supplier"), new("Contact", @"contact|email|phone", "text"), new("Certification", @"cert|iso|registration", "text"),
            new("Score or rating", @"score|rating|grade|points", "number"), new("Approval status", @"status|approved|approval", "choice"), new("Approval", @"approv|review", "signature", true),
        ]),
        new("audit", "Audit form", "audits",
        [
            new("Audit area or process", @"area|process|scope|department", "text"), new("Auditor", @"auditor|lead|audited by", "user"), new("Audit date", @"date", "date"),
            new("Requirement or clause", @"clause|requirement|criteria|question", "text"), new("Finding", @"finding|observation|nonconform|result", "longtext"),
            new("Objective evidence", @"evidence|record|sample", "longtext"), new("Auditor sign-off", @"auditor|sign|approv", "signature", true),
        ]),
        new("apqp", "APQP form", "apqp",
        [
            new("Project or program", @"project|program|part", "text"), new("Phase", @"phase|stage|gate", "choice"), new("Deliverable", @"deliverable|task|element|activity", "text"),
            new("Owner", @"owner|responsible", "user"), new("Due date", @"due|target|plan", "date"), new("Status", @"status|complete|done", "choice"), new("Gate approval", @"approv|review|gate", "signature", true),
        ]),
        new("ppap", "PPAP form", "apqp",
        [
            new("Part", @"\bpart\b|p/n", "part"), new("Customer", @"customer", "customer"), new("Submission level", @"level", "choice"),
            new("Element", @"element|requirement|item", "text"), new("Element status", @"status|accepted|approved|complete", "choice"),
            new("Submission date", @"date", "date"), new("Submission approval", @"approv|warrant|sign", "signature", true),
        ]),
    ];

    public static TargetProfile? Profile(string? key) => key is null ? null : Profiles.FirstOrDefault(p => p.Key == key);

    // ---------------------------------------------------------------- detection

    /// <summary>Builds the review items from a converted definition (Excel and Word; PDF supplies its own detections).</summary>
    public static List<ReviewItem> Detect(FormDefinition d, FidelityReport report, string source)
    {
        var items = new List<ReviewItem>();
        var pages = PageIndex(d);
        int PageOf(string? loc) { var sheet = SheetOf(loc); return sheet is not null && pages.TryGetValue(sheet, out var n) ? n : 1; }
        var fieldSource = source == "docx" ? "content-control" : "cell";

        foreach (var s in d.Sections)
        {
            items.Add(new ReviewItem
            {
                Id = $"section:{s.Key}", Kind = "section", Label = s.Title, Type = "section", Candidate = "Section", Location = s.Title, SectionId = s.Key, Confidence = s.Title.Length > 0 ? 0.9 : 0.5,
                Source = source == "docx" ? "paragraph" : "cell", Reason = s.Title.Length > 0 ? "a heading starts this part of the form" : "a part of the form without a heading",
            });
            if (!string.IsNullOrWhiteSpace(s.Instructions))
                items.Add(new ReviewItem { Id = $"instruction:{s.Key}", Kind = "instruction", Label = Trim(s.Instructions!, 200), Type = "instruction", Candidate = "Instruction", Location = s.Title, SectionId = s.Key, Confidence = 0.9, Source = "paragraph", Reason = "text that tells the person filling the form what to do" });
            if (s.VisibleWhen is not null)
                items.Add(Conditional($"section:{s.Key}", s.Title, s.VisibleWhen, s.Key));
        }
        foreach (var f in d.Fields)
        {
            if (f.Type == "instruction")
            {
                items.Add(new ReviewItem { Id = $"field:{f.Key}", Kind = "instruction", Label = Trim(f.Label, 200), Type = "instruction", Candidate = "Instruction", Location = f.SourceLocation ?? "", Page = PageOf(f.SourceLocation), SectionId = f.Section, Confidence = 0.9, Source = fieldSource, Reason = "text that tells the person filling the form what to do", CanExclude = true });
                continue;
            }
            var ev = Evidence.Contains(f.Type);
            var sug = FieldDetection.Suggest(f.Label, f.Type, ev, f.Options);
            var item = new ReviewItem
            {
                Id = $"field:{f.Key}", Kind = f.Type == "calc" ? "formula" : f.Type == "boolean" ? "checkbox" : "field", Label = f.Label, Type = f.Type, Candidate = FieldDetection.CandidateOf(f.Type),
                Location = f.SourceLocation ?? "", Page = PageOf(f.SourceLocation), SectionId = f.Section, Confidence = sug.Confidence, Source = fieldSource, Reason = sug.Reason,
                Options = f.Options, Formula = f.Formula, CanExclude = f.Type != "calc", Typed = f.Type != "calc",
            };
            if (sug.Type != f.Type) { item.Suggested = sug.Type; item.SuggestedCandidate = sug.Candidate; }
            items.Add(item);
            if (f.VisibleWhen is not null) items.Add(Conditional($"field:{f.Key}", f.Label, f.VisibleWhen, f.Section));
        }
        foreach (var t in d.Tables)
        {
            var headed = t.Columns.Count > 0 && t.Columns.All(c => c.Label.Trim().Length > 0);
            items.Add(new ReviewItem
            {
                Id = $"table:{t.Key}", Kind = "table", Label = t.Title, Type = "table", Candidate = "Repeating row", Location = t.Columns.FirstOrDefault()?.SourceLocation ?? t.Title,
                Page = PageOf(t.Columns.FirstOrDefault()?.SourceLocation), SectionId = t.Section, Confidence = headed ? 0.9 : 0.55, Source = source == "docx" ? "table" : "cell",
                Reason = headed ? $"a header row with {t.Columns.Count} columns{(t.FixedRows is { Count: > 0 } ? $" and {t.FixedRows.Count} fixed rows" : " and repeating rows")}" : "some columns have no heading",
                Columns = t.Columns.Select(c => new ReviewColumn(c.Label, c.Type, t.Totals?.Any(x => x.Column == c.Key) == true)).ToList(),
            });
            foreach (var c in t.Columns)
            {
                var ev = Evidence.Contains(c.Type);
                var sug = FieldDetection.Suggest(c.Label, c.Type, ev, c.Options);
                var item = new ReviewItem
                {
                    Id = $"column:{t.Key}.{c.Key}", Kind = c.Type == "calc" ? "formula" : "column", Label = c.Label, Type = c.Type, Candidate = FieldDetection.CandidateOf(c.Type),
                    Location = c.SourceLocation ?? t.Title, Page = PageOf(c.SourceLocation), SectionId = t.Section, Confidence = sug.Confidence, Source = source == "docx" ? "table" : "cell",
                    Reason = $"column of {t.Title}: {sug.Reason}", Options = c.Options, Formula = c.Formula, Typed = c.Type != "calc",
                };
                if (sug.Type != c.Type && sug.Type is not ("signature" or "table")) { item.Suggested = sug.Type; item.SuggestedCandidate = sug.Candidate; }
                items.Add(item);
            }
            if (t.VisibleWhen is not null) items.Add(Conditional($"table:{t.Key}", t.Title, t.VisibleWhen, t.Section));
        }
        foreach (var s in d.Signatures)
            items.Add(new ReviewItem
            {
                Id = $"signature:{s.Key}", Kind = ApprovalRoles.Contains(s.Role) ? "approval" : "signature", Label = s.Label, Type = "signature", Candidate = "Signature", Role = s.Role,
                Location = s.Label, SectionId = s.Section, Confidence = 0.9, Source = source == "docx" ? "paragraph" : "cell", CanExclude = true,
                Reason = ApprovalRoles.Contains(s.Role) ? $"a signature line for the {s.Role} step (approval block)" : $"a signature line for the {s.Role} step",
            });
        // formulas that stay only in the original: shown so nobody believes they were converted
        var n = 0;
        foreach (var i in report.Items.Where(i => i.Area == "formula" && i.Status is "not-converted" or "preserved"))
            items.Add(new ReviewItem
            {
                Id = $"formula:{++n}", Kind = "formula", Label = Trim(i.Detail, 200), Type = "calc", Candidate = "Calculated field", Location = i.Location, Page = PageOf(i.Location),
                Confidence = i.Status == "preserved" ? 0.5 : 0, Source = "cell", Reason = i.Status == "preserved" ? "kept in the original file with its stored result; not calculated in the form" : "could not be converted",
            });
        foreach (var it in items) it.MustConfirm = MustConfirm(it);
        return items;
    }

    static ReviewItem Conditional(string id, string label, FieldCondition c, string? section) => new()
    {
        Id = $"conditional:{id}", Kind = "conditional", Label = label, Type = "condition", Candidate = "Conditional area", Location = label, SectionId = section, Confidence = 0.75,
        Source = "cell", Reason = $"shown only when {c.Field} {c.Operator} {c.Value}",
    };

    /// <summary>Low-confidence and unmapped items need a person's confirmation (TECH: require user confirmation for low-confidence mappings).</summary>
    public static bool MustConfirm(ReviewItem i) => i.Confidence < Medium;

    static string Trim(string s, int max) { s = Regex.Replace(s, @"\s+", " ").Trim(); return s.Length > max ? s[..max] : s; }

    static string? SheetOf(string? loc)
    {
        if (string.IsNullOrWhiteSpace(loc)) return null;
        var bang = loc.LastIndexOf('!');
        return bang > 0 ? loc[..bang].Trim('\'') : null;
    }

    static Dictionary<string, int> PageIndex(FormDefinition d)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in d.Layout?.Blocks ?? [])
            if (b.Origin is { Length: > 0 } o && !map.ContainsKey(o)) map[o] = map.Count + 1;
        return map;
    }

    // ---------------------------------------------------------------- corrections

    /// <summary>
    /// Applies the review to the definition: kept or left out, corrected labels, types and options, confirmations. Items left out are removed
    /// with their print-layout bindings (the label text stays in the layout). Throws when the corrected form is not valid.
    /// </summary>
    public static void ApplyReview(FormDefinition d, List<ReviewItem> items, IReadOnlyDictionary<string, ReviewInput> review)
    {
        foreach (var it in items)
        {
            if (!review.TryGetValue(it.Id, out var r)) continue;
            if (r.Confirmed is { } c) it.Confirmed = c;
            if (r.Include is false && !it.CanExclude) throw new AppException($"'{it.Label}' cannot be left out; correct it instead.");
            if (r.Include is { } inc) it.Include = inc;
            if (!string.IsNullOrWhiteSpace(r.Label)) it.Label = Trim(r.Label, 200);
            if (!string.IsNullOrWhiteSpace(r.Type))
            {
                if (!it.Typed) throw new AppException($"The type of '{it.Label}' cannot be changed.");
                it.Type = r.Type; it.Candidate = FieldDetection.CandidateOf(r.Type);
            }
            if (r.Options is { Count: > 0 }) it.Options = r.Options.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).Distinct().ToList();
        }
        foreach (var it in items.Where(i => review.ContainsKey(i.Id)))
        {
            var (kind, key) = Split(it.Id);
            switch (kind)
            {
                case "field":
                {
                    var f = d.Fields.FirstOrDefault(x => x.Key == key);
                    if (f is null) break;
                    if (!it.Include) { RemoveField(d, f); break; }
                    f.Label = it.Label;
                    if (f.Type != it.Type) Retype(f, it.Type, it.Options);
                    else if (it.Options is { Count: > 0 } && f.Type is "choice" or "radio" or "multichoice") f.Options = it.Options;
                    break;
                }
                case "column":
                {
                    var dot = key.IndexOf('.');
                    var t = d.Tables.FirstOrDefault(x => x.Key == key[..dot]);
                    var c = t?.Columns.FirstOrDefault(x => x.Key == key[(dot + 1)..]);
                    if (c is null) break;
                    c.Label = it.Label;
                    if (c.Type != it.Type) Retype(c, it.Type, it.Options);
                    else if (it.Options is { Count: > 0 } && c.Type is "choice" or "radio" or "multichoice") c.Options = it.Options;
                    break;
                }
                case "table": { var t = d.Tables.FirstOrDefault(x => x.Key == key); if (t is not null) t.Title = it.Label; break; }
                case "section": { var s = d.Sections.FirstOrDefault(x => x.Key == key); if (s is not null) s.Title = it.Label; break; }
                case "signature":
                {
                    var s = d.Signatures.FirstOrDefault(x => x.Key == key);
                    if (s is null) break;
                    if (!it.Include) { RemoveSignature(d, s); break; }
                    s.Label = it.Label;
                    break;
                }
            }
        }
        foreach (var it in items) it.MustConfirm = MustConfirm(it);
        try { FormDefinitions.Validate(d); }
        catch (AppException e) { throw new AppException($"The corrected form is not valid: {e.Message}"); }
    }

    static (string Kind, string Key) Split(string id) { var i = id.IndexOf(':'); return i < 0 ? (id, "") : (id[..i], id[(i + 1)..]); }

    static void Retype(FormField f, string type, List<string>? options)
    {
        if (type is "choice" or "radio" or "multichoice")
        {
            var opts = options ?? f.Options;
            if (opts is not { Count: > 0 }) throw new AppException($"Give the options for '{f.Label}' when it becomes a list.");
            f.Options = opts;
        }
        else if (f.Type is "choice" or "radio" or "multichoice") f.Options = null;
        f.Type = type;
        if (type is not ("number" or "integer" or "currency" or "percent" or "measurement")) { f.Min = null; f.Max = null; f.Decimals = null; }
    }

    static void RemoveField(FormDefinition d, FormField f)
    {
        var token = new Regex($@"(?<![\w.]){Regex.Escape(f.Key)}(?![\w(])");
        var users = d.Fields.Where(x => x != f && ((x.Formula is { } fx && token.IsMatch(fx)) || x.VisibleWhen?.Field == f.Key || x.RequiredWhen?.Field == f.Key)).Select(x => x.Label)
            .Concat(d.Tables.SelectMany(t => t.Columns).Where(c => c.Formula is { } fx && token.IsMatch(fx)).Select(c => c.Label))
            .Concat(d.Sections.Where(s => s.VisibleWhen?.Field == f.Key).Select(s => s.Title)).ToList();
        if (users.Count > 0) throw new AppException($"'{f.Label}' is used by {string.Join(", ", users.Take(3).Select(u => $"'{u}'"))}; keep it or change those first.");
        d.Fields.Remove(f);
        d.Rules.RemoveAll(r => r.Target == f.Key);
        Unbind(d, b => b == $"field:{f.Key}" || b == $"check:{f.Key}");
    }

    static void RemoveSignature(FormDefinition d, SignatureBlock s)
    {
        d.Signatures.Remove(s);
        foreach (var o in d.Signatures) o.DistinctFrom?.Remove(s.Key);
        Unbind(d, b => b.StartsWith($"sig:{s.Key}.", StringComparison.Ordinal));
    }

    static void Unbind(FormDefinition d, Func<string, bool> match)
    {
        foreach (var cell in d.Layout?.Blocks.Where(b => b.Grid is not null).SelectMany(b => b.Grid!.Cells) ?? [])
            if (cell.Bind is { } b && match(b)) cell.Bind = null;
    }

    // ---------------------------------------------------------------- confidence and families

    public static ConfidenceSummary Summarize(IEnumerable<(double Confidence, bool Include)> src)
    {
        var l = src.Where(x => x.Include).Select(x => x.Confidence).ToList();
        if (l.Count == 0) return new("unmapped", 0, 0, 0, 0, 0, 0);
        return new(Level(l.Min()), Math.Round(l.Average(), 2), l.Count(c => Level(c) == "high"), l.Count(c => Level(c) == "medium"), l.Count(c => Level(c) == "low"), l.Count(c => Level(c) == "unmapped"), l.Count);
    }

    /// <summary>Confidence at document, page or worksheet, section, field, formula and table level.</summary>
    public static object ConfidenceLevels(List<ReviewItem> items, IReadOnlyList<string> pageNames)
    {
        ConfidenceSummary S(IEnumerable<ReviewItem> x) => Summarize(x.Select(i => (i.Confidence, i.Include)));
        return new
        {
            document = S(items),
            pages = items.GroupBy(i => i.Page).OrderBy(g => g.Key).Select(g => new { page = g.Key, name = g.Key - 1 < pageNames.Count ? pageNames[g.Key - 1] : $"Page {g.Key}", confidence = S(g) }),
            sections = items.Where(i => i.Kind == "section").Select(s => new { id = s.SectionId, title = s.Label, confidence = S(items.Where(i => i.SectionId == s.SectionId)) }),
            fields = S(items.Where(i => i.Kind is "field" or "checkbox" or "column")),
            formulas = S(items.Where(i => i.Kind == "formula")),
            tables = S(items.Where(i => i.Kind == "table")),
        };
    }

    /// <summary>Checks a converted form against a form family: which expected elements were found and which are missing.</summary>
    public static List<ProfileMatch> MatchProfile(FormDefinition d, TargetProfile p)
    {
        var fields = d.Fields.Select(f => (Key: f.Key, Label: f.Label)).Concat(d.Tables.SelectMany(t => t.Columns.Select(c => (Key: $"{t.Key}.{c.Key}", Label: c.Label)))).ToList();
        var used = new HashSet<string>();
        var list = new List<ProfileMatch>();
        foreach (var e in p.Elements)
        {
            var rx = new Regex(e.Pattern, RegexOptions.IgnoreCase);
            if (e.Signature)
            {
                var s = d.Signatures.FirstOrDefault(x => rx.IsMatch(x.Label) || rx.IsMatch(x.Role)) ?? d.Signatures.FirstOrDefault(x => ApprovalRoles.Contains(x.Role));
                list.Add(new ProfileMatch(e.Name, s?.Key, s?.Label, e.Type, true));
                continue;
            }
            var hit = fields.FirstOrDefault(f => !used.Contains(f.Key) && rx.IsMatch(f.Label));
            if (hit.Key is not null) used.Add(hit.Key);
            list.Add(new ProfileMatch(e.Name, hit.Key, hit.Label, e.Type, false));
        }
        return list;
    }
}

public sealed record ReviewInput(string Id, bool? Include, string? Label, string? Type, List<string>? Options, bool? Confirmed);
