using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Imports;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Imports.Conversion;
using QualGuardIQ.Infrastructure.Modules.Imports.Excel;

namespace QualGuardIQ.Infrastructure.Modules.Imports;

public sealed record ConversionTargetRequest(string? Target);

/// <summary>
/// The format-neutral conversion engine in the Import Center: every converted file (Excel, Word, PDF) gets the same review of detected items
/// with suggestions and confidence, the same correction and confirmation rules, an optional form family (NCR, CAPA, calibration, supplier,
/// audit, APQP, PPAP) and the 23-step conversion pipeline status.
/// </summary>
public sealed partial class ImportCenterService
{
    static Dictionary<string, ReviewInput> ConversionReview(ImportJob job)
    {
        var root = Obj(job.Settings);
        var node = root["review"] as JsonArray ?? root["pdfReview"] as JsonArray;
        return node?.Deserialize<List<ReviewInput>>(JsonOpts)!.ToDictionary(x => x.Id) ?? [];
    }

    public static string? ConversionTarget(ImportJob job) => Obj(job.Settings)["conversionTarget"]?.GetValue<string?>();

    /// <summary>Runs detection, applies the person's review, scores confidence and checks the form family; writes it all into the analysis.</summary>
    void RunEngine(ImportJob job, JsonObject analysis, FormDefinition def, FidelityReport report, string source)
    {
        var items = ConversionEngine.Detect(def, report, source);
        var review = ConversionReview(job);
        try { ConversionEngine.ApplyReview(def, items, review); }
        catch (AppException e)
        {
            AddIssue(job, "review", null, ImportIssueSeverity.Error, "REVIEW", e.Message, "Change the correction in the review and save it again.");
            items = ConversionEngine.Detect(def, report, source);
        }
        var pages = def.Layout?.Blocks.Select(b => b.Origin).Where(o => o is { Length: > 0 }).Distinct().Select(o => o!).ToList() ?? [];
        var required = items.Count(i => i.Include && i.MustConfirm && !i.Confirmed);
        analysis["detections"] = JsonSerializer.SerializeToNode(items, JsonOpts);
        analysis["review"] = new JsonObject { ["required"] = required, ["ocr"] = false, ["suggestions"] = items.Count(i => i.Suggested is not null && i.Include) };
        analysis["confidence"] = JsonSerializer.SerializeToNode(ConversionEngine.ConfidenceLevels(items, pages), JsonOpts);
        ProfileInto(job, analysis, def);
        if (required > 0) AddIssue(job, "detections", null, ImportIssueSeverity.Info, "REVIEW", $"{required} item(s) with low confidence or not converted need confirmation before the conversion can be submitted.", "Open the review, correct what is wrong and confirm.");
    }

    void ProfileInto(ImportJob job, JsonObject analysis, FormDefinition def)
    {
        var p = ConversionEngine.Profile(ConversionTarget(job));
        if (p is null) { analysis.Remove("profile"); return; }
        var m = ConversionEngine.MatchProfile(def, p);
        analysis["profile"] = JsonSerializer.SerializeToNode(new { key = p.Key, name = p.Name, module = p.ModuleKey, elements = m, found = m.Count(x => x.FieldKey is not null), missing = m.Count(x => x.FieldKey is null) }, JsonOpts);
        foreach (var x in m.Where(x => x.FieldKey is null))
            AddIssue(job, p.Name, null, ImportIssueSeverity.Info, "PROFILE", $"No {(x.Signature ? "signature block" : "field")} for '{x.Element}' was found.", $"Add it in the designer after import if this {p.Name} needs it.");
    }

    /// <summary>Chooses the form family the conversion is for (or none). The conversion is re-checked against it.</summary>
    public async Task SetConversionTargetAsync(Guid id, ConversionTargetRequest req, CancellationToken ct)
    {
        var job = await LoadAsync(id, Permissions.ImportsEdit, ct);
        if (job.ImportMode != "template") throw new AppException("A form family applies to conversions into a form.");
        if (job.Status is not (ImportJobStatus.PreviewReady or ImportJobStatus.ValidationFailed)) throw new ConflictException("The form family can be changed before the conversion is submitted.");
        var key = string.IsNullOrWhiteSpace(req.Target) ? null : req.Target.Trim().ToLowerInvariant();
        if (key is not null && ConversionEngine.Profile(key) is null) throw new AppException($"Choose one of: {string.Join(", ", ConversionEngine.Profiles.Select(p => p.Key))}.");
        var root = Obj(job.Settings);
        root["conversionTarget"] = key;
        job.Settings = root.ToJsonString();
        Event(job, "ConversionTargetChanged", key is null ? "Converted as a general form." : $"Converted as a {ConversionEngine.Profile(key)!.Name}.");
        Transition(job, ImportJobStatus.Parsing, "Form family changed");
        await db.SaveChangesAsync(ct);
        await AdvanceAsync(job, ct);
    }

    /// <summary>Corrections and confirmations for an Excel or Word conversion. Checked against the current form before they are kept.</summary>
    async Task ReviewConversionAsync(ImportJob job, DetectionReviewRequest req, CancellationToken ct)
    {
        var analysis = Obj(job.Analysis);
        var items = (analysis["detections"] as JsonArray)?.Deserialize<List<ReviewItem>>(JsonOpts) ?? [];
        var def = ConvertedDefinition(job) ?? throw new AppException("Nothing was converted from this file.");
        var known = items.Select(i => i.Id).ToHashSet();
        var review = ConversionReview(job);
        var delta = new Dictionary<string, ReviewInput>();
        foreach (var i in req.Items)
        {
            if (!known.Contains(i.Id)) throw new AppException($"Item '{i.Id}' is not part of this conversion.");
            if (i.Type is { Length: > 0 } t && !(FormDefinitions.FieldTypes.Contains(t) && t is not ("calc" or "instruction"))) throw new AppException($"'{t}' is not a field type.");
            if (i.Label is { Length: > 200 }) throw new AppException("Labels are limited to 200 characters.");
            if (i.Options is { Count: > 200 }) throw new AppException("A field can have at most 200 options.");
            delta[i.Id] = new ReviewInput(i.Id, i.Include, i.Label, i.Type, i.Options, i.Confirmed);
        }
        if (req.ConfirmAll) foreach (var k in known) delta[k] = delta.TryGetValue(k, out var d) ? d with { Confirmed = true } : new ReviewInput(k, null, null, null, null, true);
        ConversionEngine.ApplyReview(def, items, delta); // throws with the reason when a correction would break the form
        foreach (var (k, d) in delta)
        {
            var prev = review.GetValueOrDefault(k);
            review[k] = new ReviewInput(k, d.Include ?? prev?.Include, d.Label ?? prev?.Label, d.Type ?? prev?.Type, d.Options ?? prev?.Options, d.Confirmed ?? prev?.Confirmed);
        }
        var root = Obj(job.Settings);
        root["review"] = JsonSerializer.SerializeToNode(review.Values.ToList(), JsonOpts);
        job.Settings = root.ToJsonString();
        Event(job, "ConversionReviewed", $"{req.Items.Count} item(s) corrected or confirmed{(req.ConfirmAll ? "; all items confirmed" : "")}.");
        Transition(job, ImportJobStatus.Parsing, "Conversion reviewed");
        await db.SaveChangesAsync(ct);
        await AdvanceAsync(job, ct);
    }

    /// <summary>Nothing detected is published until it has been reviewed: the conversion is submitted only when no item still needs confirmation.</summary>
    static void CheckConversionReview(ImportJob job)
    {
        if (job.ImportMode != "template") return;
        if (Obj(job.Analysis)["conversion"] is null) throw new AppException("Nothing was converted from this file; keep it as a controlled document instead.");
        var pending = Obj(job.Analysis)["review"]?["required"]?.GetValue<int>() ?? 0;
        if (pending > 0)
            throw new AppException(IsPdf(job)
                ? $"Confirm the {pending} detection(s) with medium or low confidence (or read by OCR) before submitting."
                : $"Confirm the {pending} item(s) with low confidence or not converted before submitting.");
    }

    // ---------------------------------------------------------------- pipeline

    public sealed record PipelineStep(int Number, string Name, string Status, string Detail);

    static readonly string[] StepNames =
    [
        "Inspect source file", "Extract document structure", "Detect pages or worksheets", "Detect sections", "Detect labels", "Detect fields", "Detect tables",
        "Detect formulas", "Detect validations", "Detect signatures", "Detect approval blocks", "Detect instructions", "Detect conditional areas",
        "Generate mapping suggestions", "Score confidence", "Present review interface", "Allow corrections", "Generate normalized schema", "Generate preview",
        "Compare fidelity", "Validate", "Submit for approval", "Publish controlled template",
    ];

    /// <summary>The 23 conversion steps with their state for this import: done, attention (a person must act), pending, or not applicable.</summary>
    public async Task<IReadOnlyList<PipelineStep>> PipelineAsync(ImportJob job, CancellationToken ct)
    {
        var a = Obj(job.Analysis);
        var conv = a["conversion"];
        var items = (a["detections"] as JsonArray)?.Deserialize<List<JsonElement>>(JsonOpts) ?? [];
        int Count(params string[] kinds) => items.Count(i => i.TryGetProperty("kind", out var k) && kinds.Contains(k.GetString()) && (!i.TryGetProperty("include", out var inc) || inc.GetBoolean()));
        var def = ConvertedDefinition(job);
        var status = job.Status;
        var scanned = status is not (ImportJobStatus.Uploaded or ImportJobStatus.SecurityScanPending);
        var parsed = a.Count > 0 && status != ImportJobStatus.Failed;
        var pages = a["pdf"]?["pages"] is JsonArray pp ? pp.Count : a["workbook"]?["sheets"] is JsonArray ss ? ss.Count : a["document"] is not null ? Math.Max(1, def?.Layout?.Blocks.Count(b => b.Type == "pagebreak") + 1 ?? 1) : 0;
        var required = a["review"]?["required"]?.GetValue<int>() ?? 0;
        var corrections = ConversionReview(job).Count;
        var report = conv?["report"];
        var notConverted = (report?["items"] as JsonArray)?.Count(i => i?["status"]?.GetValue<string>() == "not-converted") ?? 0;
        var docId = Obj(job.Summary)["documentId"]?.GetValue<string>();
        var published = docId is not null && Guid.TryParse(docId, out var g) && await db.DocumentRevisions.AnyAsync(r => r.DocumentId == g && r.Status == Domain.Documents.RevisionStatus.Effective, ct);
        var level = a["confidence"]?["document"]?["level"]?.GetValue<string>() ?? a["pdf"]?["confidence"]?["level"]?.GetValue<string>();
        string S(bool done, bool attention = false) => attention ? "attention" : done ? "done" : "pending";
        var isTemplate = job.ImportMode == "template";
        var na = "not-applicable";
        var steps = new List<PipelineStep>
        {
            new(1, StepNames[0], status == ImportJobStatus.SecurityScanFailed ? "attention" : S(scanned), status == ImportJobStatus.SecurityScanFailed ? "the file failed the security inspection" : scanned ? $"{job.FileType.ToUpperInvariant()}, SHA-256 {job.FileHash[..12]}…, signature and content checked" : "waiting for the security inspection"),
            new(2, StepNames[1], status == ImportJobStatus.Failed ? "attention" : S(parsed), parsed ? "structure read from the file" : status == ImportJobStatus.Failed ? "the file could not be read" : "waiting"),
            new(3, StepNames[2], S(pages > 0), pages > 0 ? $"{pages} {(a["workbook"] is not null ? "worksheet(s)" : "page(s)")}" : "—"),
        };
        if (!isTemplate || conv is null)
        {
            for (var i = 3; i < StepNames.Length; i++) steps.Add(new(i + 1, StepNames[i], na, isTemplate ? "available when the file has been converted" : "applies when a file is converted into a form"));
            return steps;
        }
        var fields = def?.Fields.Count(f => f.Type != "instruction") ?? 0;
        var labels = def?.Fields.Count(f => f.Label.Trim().Length > 0) ?? 0;
        var validations = (def?.Fields.Count(f => f.Options is { Count: > 0 } || f.Min is not null || f.Max is not null || f.Pattern is not null || f.Checks is { Count: > 0 } || f.Required) ?? 0) + (def?.Rules.Count ?? 0);
        var formulas = report?["formulas"];
        steps.AddRange(
        [
            new(4, StepNames[3], "done", $"{Count("section")} section(s)"),
            new(5, StepNames[4], "done", $"{labels} labelled field(s)"),
            new(6, StepNames[5], "done", $"{fields} field(s), {Count("checkbox")} checkbox(es)"),
            new(7, StepNames[6], "done", $"{def?.Tables.Count ?? 0} table(s)"),
            new(8, StepNames[7], notConverted > 0 ? "attention" : "done", formulas is null ? "none" : $"{formulas["converted"]} of {formulas["total"]} live, {formulas["preserved"]} kept in the original, {formulas["notConverted"]} not converted"),
            new(9, StepNames[8], "done", $"{validations} validation(s) and formatting rule(s)"),
            new(10, StepNames[9], "done", $"{def?.Signatures.Count ?? 0} signature block(s)"),
            new(11, StepNames[10], "done", $"{Count("approval")} approval block(s)"),
            new(12, StepNames[11], "done", $"{Count("instruction")} instruction(s)"),
            new(13, StepNames[12], "done", $"{Count("conditional")} conditional area(s)"),
            new(14, StepNames[13], "done", $"{a["review"]?["suggestions"]?.GetValue<int>() ?? 0} type suggestion(s)"),
            new(15, StepNames[14], "done", level is null ? "scored" : $"document confidence {level}"),
            new(16, StepNames[15], "done", $"{items.Count} item(s) in the review"),
            new(17, StepNames[16], required > 0 ? "attention" : "done", required > 0 ? $"{required} item(s) to confirm; {corrections} reviewed so far" : $"{corrections} item(s) corrected or confirmed"),
            new(18, StepNames[17], S(def is not null), def is null ? "—" : $"form schema version {def.SchemaVersion}"),
            new(19, StepNames[18], S(def is not null), def?.Layout is not null ? "interactive form and original-look print layout" : "interactive form and standardized print layout"),
            new(20, StepNames[19], S(report is not null), $"{(report?["items"] as JsonArray)?.Count ?? 0} fidelity item(s) to review"),
            new(21, StepNames[20], required > 0 || status == ImportJobStatus.ValidationFailed ? "attention" : S(status >= ImportJobStatus.PreviewReady), required > 0 ? "items still need confirmation" : "the converted form is valid"),
            new(22, StepNames[21], S(status is >= ImportJobStatus.AwaitingApproval and not ImportJobStatus.Canceled), status == ImportJobStatus.AwaitingApproval ? "waiting for an approver to accept the fidelity report" : status > ImportJobStatus.AwaitingApproval ? "approved, fidelity report accepted" : "not yet submitted"),
            new(23, StepNames[22], published ? "done" : docId is not null ? "attention" : "pending", published ? "the template revision is effective" : docId is not null ? $"draft template {Obj(job.Summary)["documentNumber"]} created; it is published through template approval" : "after the import is approved"),
        ]);
        return steps;
    }
}
