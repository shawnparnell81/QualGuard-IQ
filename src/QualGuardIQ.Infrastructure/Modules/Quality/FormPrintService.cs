using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Modules.Appearance;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

/// <summary>Printable output for form records and form template revisions (standardized or original-look). Every print is audited.</summary>
public sealed class FormPrintService(AppDbContext db, ModuleSupport m, ICurrentUser current, IRecordRegistry records)
{
    public sealed record PrintResult(byte[] Pdf, string FileName, string Style);

    static string? Style(string? s) => s is null or "" ? null : s is "standardized" or "original" ? s : throw new AppException("Print style must be standardized or original.");

    public async Task<PrintResult> RecordAsync(Guid id, string? style, CancellationToken ct)
    {
        style = Style(style);
        await m.ViewAsync(FormService.RecordType, id, ct);
        var r = await db.FormRecords.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var schema = await db.DocumentRevisions.Where(x => x.Id == r.TemplateRevisionId).Select(x => x.FieldSchema).FirstAsync(ct);
        var def = await FormFamily.ApplyAsync(db, r.TemplateId, FormDefinitions.Parse(schema!), ct);
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(r.ValuesJson) ?? [];
        var zone = await m.ZoneAsync(r.SiteId, ct);
        string Local(DateTimeOffset t) => $"{TimeZoneInfo.ConvertTime(t, zone):yyyy-MM-dd HH:mm} {Abbrev(zone, t)}";
        var sigs = await db.FormSignatures.AsNoTracking().Where(s => s.FormRecordId == id).OrderBy(s => s.SignedAt).ToListAsync(ct);
        var ctx = new FormPrintContext(await OrgAsync(ct), string.IsNullOrWhiteSpace(r.Title) ? r.TemplateNumber : r.Title, r.TemplateNumber, r.TemplateRevision, r.Number,
            r.Status.ToString(), await SiteAsync(r.SiteId, ct), Local(m.Now), current.UserName, def.Page.Confidentiality,
            r.Status == FormRecordStatus.Approved ? "OFFICIAL RECORD - approved electronically in QualGuard IQ" : "UNCONTROLLED WHEN PRINTED - record not yet approved",
            r.Status switch { FormRecordStatus.Draft => "DRAFT", FormRecordStatus.Rejected => "REJECTED", FormRecordStatus.Submitted => "IN REVIEW", _ => null },
            $"QG:REC:{FormService.RecordType}:{r.Id}");
        var renderer = new FormPdfRenderer(def, values, sigs.Select(s => new PrintedSignature(s.SignatureKey, s.SignerName, Local(s.SignedAt), s.Meaning, s.Verification, !s.IsInvalidated)), ctx)
            { Palette = await AppearanceModel.PrintColoursAsync(db, FormThemes.Of(def), ct) };
        var used = renderer.Effective(style);
        var pdf = renderer.Render(used);
        await AuditAsync(nameof(FormRecord), id, r.SiteId, $"Printed form record {r.Number} ({used} output) as PDF", ct);
        return new PrintResult(pdf, $"{r.Number}.pdf", used);
    }

    /// <summary>Blank template revision. Effective revisions print as controlled; drafts and retired revisions carry a status watermark.</summary>
    public async Task<PrintResult> TemplateAsync(Guid revisionId, string? style, CancellationToken ct)
    {
        style = Style(style);
        var rev = await db.DocumentRevisions.AsNoTracking().Include(x => x.Document).FirstOrDefaultAsync(x => x.Id == revisionId, ct) ?? throw new NotFoundException("Revision");
        var doc = rev.Document!;
        if (doc.Kind != DocumentKind.FormTemplate || string.IsNullOrWhiteSpace(rev.FieldSchema)) throw new AppException("This revision is not a form template.");
        await records.RequireViewAsync(DocumentRecordTypes.ForDocument(doc.Kind), doc.Id, ct);
        var def = await FormFamily.ApplyAsync(db, doc.Id, FormDefinitions.Parse(rev.FieldSchema), ct);
        var zone = await m.ZoneAsync(doc.SiteId, ct);
        var ctx = new FormPrintContext(await OrgAsync(ct), doc.Title, doc.Number, rev.Revision, null, rev.Status.ToString(), await SiteAsync(doc.SiteId, ct),
            $"{TimeZoneInfo.ConvertTime(m.Now, zone):yyyy-MM-dd HH:mm} {Abbrev(zone, m.Now)}", current.UserName, def.Page.Confidentiality,
            rev.Status == RevisionStatus.Effective ? "CONTROLLED COPY - verify this is the current revision before use" : "UNCONTROLLED COPY",
            rev.Status switch { RevisionStatus.Draft or RevisionStatus.InReview => "DRAFT", RevisionStatus.Approved => "NOT YET EFFECTIVE", RevisionStatus.Superseded => "SUPERSEDED", RevisionStatus.Obsolete => "OBSOLETE", _ => null },
            $"QG:REC:{DocumentRecordTypes.ForDocument(doc.Kind)}:{doc.Id}");
        var renderer = new FormPdfRenderer(def, new Dictionary<string, string?>(), [], ctx) { Palette = await AppearanceModel.PrintColoursAsync(db, FormThemes.Of(def), ct) };
        var used = renderer.Effective(style);
        var pdf = renderer.Render(used);
        await AuditAsync(nameof(DocumentRevision), rev.Id, doc.SiteId, $"Printed form template {doc.Number} rev {rev.Revision} ({used} output) as PDF", ct);
        return new PrintResult(pdf, $"{doc.Number}-rev-{rev.Revision}.pdf", used);
    }

    /// <summary>Print preview of an unsaved definition from the designer, watermarked PREVIEW. Not a controlled print, so nothing is audited.</summary>
    public async Task<PrintResult> PreviewAsync(string schema, string? style, IReadOnlyDictionary<string, string?>? values, CancellationToken ct)
    {
        style = Style(style);
        if (string.IsNullOrWhiteSpace(schema) || schema.Length > 4_000_000) throw new AppException("The form definition is missing or too large.");
        var def = FormDefinitions.Parse(schema);
        FormDefinitions.Validate(def);
        var computed = values is { Count: > 0 } ? FormDefinitions.Apply(def, values, requireComplete: false) : new Dictionary<string, string?>();
        var ctx = new FormPrintContext(await OrgAsync(ct), "Form preview", "PREVIEW", "-", null, "Preview", null, $"{m.Now:yyyy-MM-dd HH:mm} UTC", current.UserName, def.Page.Confidentiality,
            "UNCONTROLLED - design preview", "PREVIEW", null);
        var r = new FormPdfRenderer(def, computed, [], ctx) { Palette = await AppearanceModel.PrintColoursAsync(db, FormThemes.Of(def), ct) };
        var used = r.Effective(style);
        return new PrintResult(r.Render(used), "form-preview.pdf", used);
    }

    static string Abbrev(TimeZoneInfo z, DateTimeOffset t)
    {
        var off = z.GetUtcOffset(t);
        return off == TimeSpan.Zero ? "UTC" : $"UTC{(off < TimeSpan.Zero ? "-" : "+")}{off:hh\\:mm}";
    }

    async Task<string> OrgAsync(CancellationToken ct) => await db.OrganizationSettings.AsNoTracking().Select(s => s.OrganizationName).FirstOrDefaultAsync(ct) ?? "";
    async Task<string?> SiteAsync(Guid siteId, CancellationToken ct) => await db.Sites.Where(s => s.Id == siteId).Select(s => s.Code + " " + s.Name).FirstOrDefaultAsync(ct);

    async Task AuditAsync(string type, Guid id, Guid siteId, string description, CancellationToken ct)
    {
        db.AuditEntries.Add(new AuditEntry { OccurredAt = m.Now, UserId = current.UserId, UserName = current.UserName, Action = "Printed", EntityType = type, EntityId = id, SiteId = siteId, Description = description, CorrelationId = current.CorrelationId });
        await db.SaveChangesAsync(ct);
    }
}
