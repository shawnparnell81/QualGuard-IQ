using System.Reflection;
using System.Text.RegularExpressions;
using QualGuardIQ.Application.Forms;

namespace QualGuardIQ.Infrastructure.Modules.Templates;

/// <summary>A template family in the library (filed as a template category when a template from it is installed).</summary>
public sealed record LibraryCategory(string Code, string Name, string ModuleKey, int Order, string Description);

/// <summary>An industry template package: the templates it installs, by library key or by category.</summary>
public sealed record LibraryPack(string Key, string Name, string Description, IReadOnlyList<string> Categories, IReadOnlyList<string> Templates);

/// <summary>One library template: catalog metadata plus the complete form definition it installs as.</summary>
public sealed record LibraryTemplate(string Key, string Title, string Category, string ModuleKey, string Purpose, string Scope, string Instructions, string Workflow,
    string Retention, IReadOnlyList<string> Packs, IReadOnlyList<string> Tags, FormDefinition Definition)
{
    public int FieldCount => Definition.Fields.Count(f => f.Type != "instruction") + Definition.Tables.Sum(t => t.Columns.Count);
}

/// <summary>
/// The enterprise template library: original, organization-configurable template definitions for every template family. The content lives in
/// embedded .qgt files (one per family) in a compact line format; every template is wrapped in the common record structure (purpose, scope,
/// instructions, record identification, ownership, site, department, product, part, supplier, customer, dates, status, workflow, related records,
/// attachments, signatures, audit history and retention) and validated as a form definition when the library loads.
/// </summary>
public static class TemplateLibrary
{
    /// <summary>Version of the built-in library content; installed packages record the version they came from.</summary>
    public const string Version = "2026.10";

    public static readonly IReadOnlyList<LibraryCategory> Categories =
    [
        new("NONCONFORMANCE", "Nonconformance", "ncr", 300, "Nonconformance reports, containment, segregation and disposition authorizations."),
        new("CAPA", "CAPA", "capa", 310, "Corrective and preventive action from request to closure."),
        new("ROOT_CAUSE", "Root cause and problem solving", "capa", 320, "Structured cause analysis methods and the eight discipline package."),
        new("APQP", "APQP", "apqp", 330, "Advanced product quality planning records, organization-authored."),
        new("PPAP", "PPAP", "apqp", 340, "Production part approval submission package elements, configurable by the organization."),
        new("FAI", "First article inspection", "fai", 350, "First article inspection package records."),
        new("INSPECTION", "AQL and inspection", "inspection", 360, "Receiving, in-process, final and sampling inspection records."),
        new("CALIBRATION", "Calibration and measurement systems", "calibration", 370, "Calibration control and measurement system analysis."),
        new("DOCUMENT_CONTROL", "Document control", "document-control", 380, "Master lists, registers and change records for controlled information."),
        new("AUDIT", "Audits", "audits", 390, "Audit programme, planning, execution, reporting and follow-up."),
        new("TRAINING", "Training and competence", "training", 400, "Training needs, delivery, assessment and qualification."),
        new("SUPPLIER", "Supplier quality", "suppliers", 410, "Supplier approval, monitoring, development and change control."),
        new("CUSTOMER_QUALITY", "Customer quality", "customer-quality", 420, "Complaints, returns, field performance and customer requirements."),
        new("RISK_CHANGE", "Risk and change", "risks", 430, "Risk and opportunity registers, assessments, change control and deviations."),
        new("MANAGEMENT_REVIEW", "Management review", "management-review", 440, "Management review inputs, records, decisions and actions."),
        new("DESIGN_DEVICE", "Design and device history", "design-control", 450, "Design history, device master and device history records and validation."),
        new("CONFIGURATION", "Configuration and process control", "configuration", 460, "Configuration control and special process verification."),
    ];

    static readonly Lazy<(IReadOnlyList<LibraryTemplate> Templates, IReadOnlyList<LibraryPack> Packs)> Loaded = new(Load);

    public static IReadOnlyList<LibraryTemplate> Templates => Loaded.Value.Templates;
    public static IReadOnlyList<LibraryPack> Packs => Loaded.Value.Packs;
    public static LibraryTemplate? Find(string key) => Templates.FirstOrDefault(t => t.Key == key);
    public static LibraryCategory Category(string code) => Categories.First(c => c.Code == code);

    /// <summary>The library templates a pack installs: those tagged with the pack, plus every template in the pack's categories.</summary>
    public static IReadOnlyList<LibraryTemplate> PackTemplates(LibraryPack p) =>
        Templates.Where(t => t.Packs.Contains(p.Key) || p.Categories.Contains(t.Category) || p.Templates.Contains(t.Key)).ToList();

    static (IReadOnlyList<LibraryTemplate>, IReadOnlyList<LibraryPack>) Load()
    {
        var asm = typeof(TemplateLibrary).Assembly;
        var names = asm.GetManifestResourceNames().Where(n => n.StartsWith("template-library/", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var blocks = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var templates = new List<(string File, List<string> Lines)>();
        var packs = new List<LibraryPack>();
        foreach (var n in names)
        {
            using var s = asm.GetManifestResourceStream(n)!; using var r = new StreamReader(s);
            var lines = r.ReadToEnd().Replace("\r", "").Split('\n');
            List<string>? cur = null;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                if (cur is null)
                {
                    if (line.StartsWith("block ")) { cur = []; blocks[line[6..].Trim()] = cur; continue; }
                    if (line.StartsWith("template ")) { cur = [line]; templates.Add((n, cur)); continue; }
                    if (line.StartsWith("pack ")) { packs.Add(ParsePack(line[5..])); continue; }
                    throw new InvalidOperationException($"{n}: unexpected line '{line}'.");
                }
                if (line == "end") { cur = null; continue; }
                cur.Add(line);
            }
            if (cur is not null) throw new InvalidOperationException($"{n}: missing 'end'.");
        }
        var built = templates.Select(t =>
        {
            try { return Build(t.Lines, blocks); }
            catch (Exception e) { throw new InvalidOperationException($"{t.File}: {t.Lines[0]}: {e.Message}", e); }
        }).ToList();
        var dup = built.GroupBy(t => t.Key).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidOperationException($"Template key '{dup.Key}' is used twice.");
        return (built, packs);
    }

    static string[] Cells(string s) => s.Split('|').Select(x => x.Trim()).ToArray();
    static List<string> List(string s) => s.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    static LibraryPack ParsePack(string s)
    {
        // pack key | Name | Description | categories | extra template keys
        var c = Cells(s);
        return new LibraryPack(c[0], c[1], c[2], c.Length > 3 ? List(c[3]) : [], c.Length > 4 ? List(c[4]) : []);
    }

    sealed class Ctx
    {
        public FormDefinition D = new();
        public string? Section;
        public FormTable? Table;
        public readonly List<SignatureBlock> Signs = [];
    }

    static readonly Regex Options = new(@"\[(.*?)\]", RegexOptions.Compiled);
    static readonly Regex Formula = new(@"\{(.*?)\}", RegexOptions.Compiled);

    /// <summary>Parses "* [A; B] {a + b} unit=mm min=0 max=10 dec=2 w=6 when=key=value reqwhen=key=value labels=PASS/FAIL".</summary>
    static FormField Field(string[] c)
    {
        var f = new FormField { Key = c[0], Label = c[1], Type = c.Length > 2 && c[2].Length > 0 ? c[2] : "text" };
        var opts = c.Length > 3 ? c[3] : "";
        if (Options.Match(opts) is { Success: true } o) { f.Options = List(o.Groups[1].Value.Replace(",", "\u0001")).Select(x => x.Replace("\u0001", ",")).ToList(); opts = opts.Replace(o.Value, " "); }
        if (Formula.Match(opts) is { Success: true } fm) { f.Formula = fm.Groups[1].Value.Trim(); opts = opts.Replace(fm.Value, " "); }
        foreach (var tok in opts.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (tok == "*") { f.Required = true; continue; }
            var i = tok.IndexOf('='); if (i < 0) throw new InvalidOperationException($"Field '{f.Key}': unknown option '{tok}'.");
            var (k, v) = (tok[..i], tok[(i + 1)..]);
            switch (k)
            {
                case "unit": f.Unit = v.Replace('_', ' '); break;
                case "min": f.Min = decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                case "max": f.Max = decimal.Parse(v, System.Globalization.CultureInfo.InvariantCulture); break;
                case "dec": f.Decimals = int.Parse(v); break;
                case "w": f.Width = int.Parse(v); break;
                case "len": f.MaxLength = int.Parse(v); break;
                case "labels": f.ResultLabels = v.Split('/').ToList(); break;
                case "when": f.VisibleWhen = Cond(v); break;
                case "reqwhen": f.RequiredWhen = Cond(v); break;
                default: throw new InvalidOperationException($"Field '{f.Key}': unknown option '{k}'.");
            }
        }
        if (c.Length > 4 && c[4].Length > 0) f.Help = c[4];
        if (f.Type is "choice" or "radio" or "multichoice" && (f.Options is null || f.Options.Count == 0)) throw new InvalidOperationException($"Field '{f.Key}' needs options.");
        return f;
    }

    static FieldCondition Cond(string v)
    {
        // key=value (equals), key!=value, key~a/b (in)
        if (v.Contains("!=")) { var p = v.Split("!=", 2); return new FieldCondition { Field = p[0], Operator = "notEquals", Value = p[1].Replace('_', ' ') }; }
        if (v.Contains('~')) { var p = v.Split('~', 2); return new FieldCondition { Field = p[0], Operator = "in", Value = string.Join(",", p[1].Split('/').Select(x => x.Replace('_', ' '))) }; }
        var q = v.Split('=', 2); return new FieldCondition { Field = q[0], Operator = "equals", Value = q[1].Replace('_', ' ') };
    }

    static void Apply(Ctx x, string line, Dictionary<string, List<string>> blocks, HashSet<string> used)
    {
        var sp = line.IndexOf(' '); var verb = sp < 0 ? line : line[..sp]; var rest = sp < 0 ? "" : line[(sp + 1)..];
        var c = Cells(rest);
        FormTable Last() => x.Table ?? x.D.Tables.LastOrDefault() ?? throw new InvalidOperationException($"'{verb}' needs a table before it.");
        switch (verb)
        {
            case "use":
                foreach (var b in List(rest))
                {
                    if (!blocks.TryGetValue(b, out var bl)) throw new InvalidOperationException($"Unknown block '{b}'.");
                    if (!used.Add(b)) throw new InvalidOperationException($"Block '{b}' is used twice.");
                    foreach (var l in bl) Apply(x, l, blocks, used);
                }
                break;
            case "section":
                x.D.Sections.Add(new FormSection { Key = "sec_" + c[0], Title = c[1], Columns = c.Length > 2 && c[2].Length > 0 ? int.Parse(c[2]) : 2, Instructions = c.Length > 3 && c[3].Length > 0 ? c[3] : null });
                x.Section = "sec_" + c[0]; x.Table = null;
                break;
            case "repeat":
                {
                    var s = x.D.Sections[^1]; s.Repeatable = true; s.RepeatLabel = c[0]; s.MinRepeat = c.Length > 1 ? int.Parse(c[1]) : 1; s.MaxRepeat = c.Length > 2 ? int.Parse(c[2]) : 50;
                    break;
                }
            case "showwhen": x.D.Sections[^1].VisibleWhen = Cond(rest.Trim()); break;
            case "landscape-section": x.D.Sections[^1].Orientation = "landscape"; break;
            case "f":
                {
                    var f = Field(c); f.Section = x.Section ?? throw new InvalidOperationException($"Field '{f.Key}' has no section.");
                    x.D.Fields.Add(f); x.Table = null;
                    break;
                }
            case "note":
                {
                    var key = $"note_{x.D.Fields.Count(f => f.Type == "instruction") + 1}";
                    x.D.Fields.Add(new FormField { Key = key, Label = "Guidance", Type = "instruction", Help = rest.Trim(), Section = x.Section });
                    break;
                }
            case "table":
                x.Table = new FormTable { Key = c[0], Title = c[1], Section = x.Section, MinRows = c.Length > 2 && c[2].Length > 0 ? int.Parse(c[2]) : 0, Instructions = c.Length > 3 && c[3].Length > 0 ? c[3] : null };
                x.D.Tables.Add(x.Table);
                break;
            case "c":
                (x.Table ?? throw new InvalidOperationException("Column outside a table.")).Columns.Add(Field(c));
                break;
            case "rows": Last().FixedRows = List(rest); Last().MinRows = 0; break;
            case "groups": Last().HeaderGroups = List(rest).Select(g => { var p = g.Split(':'); return new HeaderGroup { Label = p[0].Trim(), Span = int.Parse(p[1]) }; }).ToList(); break;
            case "total": (Last().Totals ??= []).Add(new TableTotal { Column = c[0], Function = c[1], Label = c.Length > 2 ? c[2] : null }); break;
            case "running": (Last().RunningTotals ??= []).Add(c[0]); break;
            case "tablewhen": Last().VisibleWhen = Cond(rest.Trim()); break;
            case "sign":
                x.Signs.Add(new SignatureBlock { Key = c[0], Label = c[1], Role = c[2], Meaning = c[3], RequiredPermission = c.Length > 4 && c[4].Length > 0 ? c[4] : null,
                    Required = !(c.Length > 5 && c[5] == "optional") });
                break;
            case "rule":
                {
                    var style = new CellStyle { Fill = c[3], Bold = c.Length > 4 && c[4] == "bold" };
                    x.D.Rules.Add(new FormatRule { Target = c[0], Operator = c[1], Value = c[2].Length > 0 ? c[2] : null, Style = style });
                    break;
                }
            case "check":
                {
                    // check field | operator | other field or value | message
                    var f = x.D.Fields.FirstOrDefault(z => z.Key == c[0]) ?? throw new InvalidOperationException($"Check on unknown field '{c[0]}'.");
                    var isField = x.D.Fields.Any(z => z.Key == c[2]);
                    (f.Checks ??= []).Add(new FieldCheck { Operator = c[1], Field = isField ? c[2] : null, Value = isField ? null : c[2], Message = c[3] });
                    break;
                }
            default: throw new InvalidOperationException($"Unknown line '{line}'.");
        }
    }

    static readonly string[] DefaultStatus = ["Draft", "Open", "In progress", "Awaiting approval", "Approved", "Closed", "Cancelled"];

    static LibraryTemplate Build(List<string> lines, Dictionary<string, List<string>> blocks)
    {
        // template key | Title | CATEGORY
        var h = Cells(lines[0][9..]);
        var (key, title, category) = (h[0], h[1], h[2]);
        var cat = Categories.FirstOrDefault(z => z.Code == category) ?? throw new InvalidOperationException($"Unknown category '{category}'.");
        string purpose = "", scope = "", instructions = "", workflow = "", retention = "";
        List<string> packs = [], tags = [], status = [.. DefaultStatus];
        var landscape = false; var confidentiality = "Internal";
        var x = new Ctx(); var used = new HashSet<string>();
        var body = new List<string>();
        foreach (var l in lines.Skip(1))
        {
            var sp = l.IndexOf(' '); var verb = sp < 0 ? l : l[..sp]; var rest = sp < 0 ? "" : l[(sp + 1)..].Trim();
            switch (verb)
            {
                case "purpose": purpose = rest; break;
                case "scope": scope = rest; break;
                case "instructions": instructions = rest; break;
                case "workflow": workflow = rest; break;
                case "retention": retention = rest; break;
                case "packs": packs = List(rest); break;
                case "tags": tags = List(rest); break;
                case "status": status = List(rest); break;
                case "landscape": landscape = true; break;
                case "confidential": confidentiality = "Confidential"; break;
                default: body.Add(l); break;
            }
        }
        foreach (var (v, n) in new[] { (purpose, "purpose"), (scope, "scope"), (instructions, "instructions"), (workflow, "workflow"), (retention, "retention") })
            if (v.Length == 0) throw new InvalidOperationException($"Template '{key}' has no {n}.");

        var d = x.D;
        d.Page = new PageSetup { Size = "Letter", Orientation = landscape ? "landscape" : "portrait", Confidentiality = confidentiality, PrintStyle = "standardized",
            Header = new BandText { Left = "{template}", Right = "Revision {revision}" },
            Footer = new BandText { Left = "{template} rev {revision} | {confidentiality}", Center = "Printed {printed}", Right = "Page {page} of {pages}" } };

        // purpose, scope and instructions
        d.Sections.Add(new FormSection { Key = "about", Title = "Purpose, scope and instructions", Columns = 1 });
        d.Fields.Add(new FormField { Key = "about_purpose", Label = "Purpose", Type = "instruction", Help = $"Purpose: {purpose}", Section = "about" });
        d.Fields.Add(new FormField { Key = "about_scope", Label = "Scope", Type = "instruction", Help = $"Scope: {scope}", Section = "about" });
        d.Fields.Add(new FormField { Key = "about_instructions", Label = "Instructions", Type = "instruction", Help = $"Instructions: {instructions}", Section = "about" });
        d.Fields.Add(new FormField { Key = "about_workflow", Label = "Workflow", Type = "instruction", Help = $"Workflow: {workflow}", Section = "about" });

        // record identification, ownership, organization and dates
        d.Sections.Add(new FormSection { Key = "identification", Title = "Record identification", Columns = 3 });
        void Id(string k, string label, string type, bool req = false, string? help = null, List<string>? options = null) =>
            d.Fields.Add(new FormField { Key = k, Label = label, Type = type, Required = req, Help = help, Options = options, Section = "identification" });
        Id("record_title", "Record title", "text", true, "A short description that identifies this record in lists and reports.");
        Id("reference_no", "Reference number", "text", false, "Assigned by the numbering scheme when the record is saved; enter the legacy number when migrating an existing record.");
        Id("record_status", "Status", "choice", true, "Moves with the workflow; closing requires the closure signatures.", status);
        Id("site", "Site", "site", true);
        Id("department", "Department", "department");
        Id("owner", "Record owner", "user", true, "Accountable for completing the record and its actions.");
        Id("product", "Product", "product");
        Id("part", "Part", "part");
        Id("supplier", "Supplier", "supplier");
        Id("customer", "Customer", "customer");
        Id("opened_on", "Date opened", "date", true);
        Id("due_on", "Target completion date", "date");
        Id("closed_on", "Date closed", "date", false, "Entered when the record is closed.");
        d.Fields.Single(f => f.Key == "closed_on").Checks = [new FieldCheck { Operator = "greaterOrEqual", Field = "opened_on", Message = "The closing date cannot be before the date opened." }];
        d.Fields.Single(f => f.Key == "due_on").Checks = [new FieldCheck { Operator = "greaterOrEqual", Field = "opened_on", Message = "The target date cannot be before the date opened." }];

        foreach (var l in body) Apply(x, l, blocks, used);

        // related records and attachments
        d.Sections.Add(new FormSection { Key = "related", Title = "Related records and attachments", Columns = 2 });
        d.Tables.Add(new FormTable { Key = "related_records", Title = "Related records", Section = "related", Instructions = "Link the records this one was raised from, raised against or verified by.",
            Columns =
            [
                new FormField { Key = "related_record", Label = "Record", Type = "record", Required = true },
                new FormField { Key = "relationship", Label = "Relationship", Type = "choice", Required = true, Options = ["Raised from", "Raised against", "Caused by", "Causes", "Verified by", "Supersedes", "Superseded by", "Related to"] },
                new FormField { Key = "related_note", Label = "Note", Type = "text" },
            ] });
        d.Fields.Add(new FormField { Key = "supporting_files", Label = "Supporting files", Type = "attachment", Section = "related", Help = "Evidence, data, drawings and correspondence. Files are virus-scanned and kept with the record." });
        d.Fields.Add(new FormField { Key = "photos", Label = "Photos", Type = "photo", Section = "related" });

        // signatures
        d.Sections.Add(new FormSection { Key = "approvals", Title = "Signatures", Columns = 2, Instructions = "Each signature is applied with re-authentication and records the signer, date, time and meaning. Editing a signed section invalidates the signatures that follow it.", KeepTogether = true });
        var signs = x.Signs.Count > 0 ? x.Signs :
        [
            new SignatureBlock { Key = "prepared_by", Label = "Prepared by", Role = "prepared", Meaning = "I prepared this record and the information in it is complete and accurate." },
            new SignatureBlock { Key = "reviewed_by", Label = "Reviewed by", Role = "reviewed", Meaning = "I reviewed this record and agree with its content." },
            new SignatureBlock { Key = "approved_by", Label = "Approved by", Role = "approved", Meaning = "I approve this record." },
        ];
        var order = 0;
        foreach (var s in signs)
        {
            s.Section = "approvals"; s.Order = ++order;
            // the first signer prepares; later signers must be other people
            if (order > 1 && s.Role is "reviewed" or "approved" or "verified") s.DistinctFrom = signs.Take(order - 1).Where(p => p.Role is "prepared" or "performed").Select(p => p.Key).ToList() is { Count: > 0 } l ? l : null;
            d.Signatures.Add(s);
        }

        // audit history and retention
        d.Sections.Add(new FormSection { Key = "record_control", Title = "Record control", Columns = 1 });
        d.Fields.Add(new FormField { Key = "record_retention", Label = "Retention", Type = "instruction", Section = "record_control",
            Help = $"Retention: {retention} Disposal follows the organization's retention schedule and is blocked while a legal hold applies." });
        d.Fields.Add(new FormField { Key = "record_audit", Label = "Audit history", Type = "instruction", Section = "record_control",
            Help = "Audit history: every entry, change, status move and signature on this record is kept in its audit history with the user, date, time and reason, and can be printed or exported with the record." });
        d.Fields.Add(new FormField { Key = "record_export", Label = "Print and export", Type = "instruction", Section = "record_control",
            Help = $"Print and export: prints on {d.Page.Size} {d.Page.Orientation} with the template revision, confidentiality marking and page numbers on every page; exports to PDF and to an Excel workbook with one sheet per table." });

        FormDefinitions.Validate(d);
        return new LibraryTemplate(key, title, cat.Code, cat.ModuleKey, purpose, scope, instructions, workflow, retention, packs, tags, d);
    }
}
