using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Application.Records;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Apqp;
using QualGuardIQ.Infrastructure.Modules.Changes;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

public sealed record ImpactItem(string Category, string RecordType, Guid RecordId, string Number, string Title, string? Detail, string RequiredAction);
public sealed record ImpactArea(string Area, List<ImpactItem> Items);

/// <summary>Document change impact (procedures, work instructions, forms, training, process, audits) and change-request impact (products, drawings, specifications, FMEA, control plans, training, validation).</summary>
public sealed class ImpactService(AppDbContext db, ModuleSupport m, ChangeService changes)
{
    async Task<List<(string Type, Guid Id, string LinkType)>> LinksAsync(string type, Guid id, CancellationToken ct) =>
        (await db.RecordLinks.AsNoTracking().Where(l => (l.FromType == type && l.FromId == id) || (l.ToType == type && l.ToId == id)).ToListAsync(ct))
        .Select(l => l.FromType == type && l.FromId == id ? (l.ToType, l.ToId, l.LinkType) : (l.FromType, l.FromId, l.LinkType)).ToList();

    public async Task<object> DocumentImpactAsync(Guid documentId, CancellationToken ct)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId, ct) ?? throw new NotFoundException("Document");
        await m.ViewAsync(doc.Kind == DocumentKind.Drawing ? "Drawing" : doc.Kind == DocumentKind.FormTemplate ? "FormTemplate" : "Document", documentId, ct);
        var links = await LinksAsync(doc.Kind == DocumentKind.Drawing ? "Drawing" : doc.Kind == DocumentKind.FormTemplate ? "FormTemplate" : "Document", documentId, ct);
        var linkedDocIds = links.Where(l => l.Type is "Document" or "FormTemplate" or "Drawing").Select(l => l.Id).ToList();
        var related = await db.Documents.AsNoTracking().Where(d => linkedDocIds.Contains(d.Id) || (doc.PartNumber != null && d.PartNumber == doc.PartNumber && d.Id != doc.Id)).ToListAsync(ct);
        static bool IsWi(ControlledDocument d) => d.DocumentType.Contains("Work Instruction", StringComparison.OrdinalIgnoreCase);
        var procedures = related.Where(d => d.Kind == DocumentKind.Document && !IsWi(d)).Select(d => new ImpactItem("Related procedure", "Document", d.Id, d.Number, d.Title, d.DocumentType, "Review for consistency")).ToList();
        var wis = related.Where(d => d.Kind == DocumentKind.Document && IsWi(d)).Select(d => new ImpactItem("Related work instruction", "Document", d.Id, d.Number, d.Title, null, "Review for consistency")).ToList();
        var forms = related.Where(d => d.Kind == DocumentKind.FormTemplate).Select(d => new ImpactItem("Related form", "FormTemplate", d.Id, d.Number, d.Title, null, "Update fields if the procedure changes what is recorded")).ToList();
        if (doc.Kind == DocumentKind.FormTemplate)
        {
            var open = await db.FormRecords.AsNoTracking().CountAsync(f => f.TemplateId == doc.Id && f.Status == FormRecordStatus.Draft, ct);
            if (open > 0) forms.Add(new ImpactItem("Open form records", "FormTemplate", doc.Id, doc.Number, $"{open} draft record(s) on the current revision", null, "Finish or restart drafts after release"));
        }
        // Training impact: courses on this document and the people who would be retrained.
        var courses = await db.TrainingCourses.AsNoTracking().Where(c => c.DocumentId == documentId && !c.IsArchived).ToListAsync(ct);
        var training = new List<ImpactItem>();
        foreach (var c in courses)
        {
            var trainees = await db.TrainingAssignments.AsNoTracking().Where(a => a.CourseId == c.Id && (a.Status == AssignmentStatus.Completed || a.Status == AssignmentStatus.Assigned)).Select(a => a.UserId).Distinct().CountAsync(ct);
            training.Add(new ImpactItem("Training impact", "TrainingCourse", c.Id, c.Number, c.Title, $"{trainees} trainee(s){(c.RetrainOnRevision ? ", retrained automatically when a revision needing training is released" : ", automatic retraining is off")}", "Retrain affected people"));
        }
        var standards = await db.CorporateStandards.AsNoTracking().Where(s => s.DocumentId == documentId).ToListAsync(ct);
        foreach (var s in standards)
        {
            var sites = await db.StandardRollouts.AsNoTracking().CountAsync(r => r.StandardId == s.Id, ct);
            training.Add(new ImpactItem("Corporate rollout", EnterpriseService.StandardType, s.Id, s.Number, s.Title, $"Rolled out to {sites} site(s)", "Notify plants and re-verify the rollout"));
        }
        // Process impact: flow steps using it as work instruction, and linked APQP documents.
        var steps = await db.ProcessFlowSteps.AsNoTracking().Where(s => s.WorkInstructionId == documentId).Join(db.ProcessFlows.Where(f => f.Status != ApqpStatus.Superseded && f.Status != ApqpStatus.Obsolete), s => s.FlowId, f => f.Id, (s, f) => new { f.Id, f.Number, f.Revision, s.Sequence, s.Name }).ToListAsync(ct);
        var process = steps.Select(s => new ImpactItem("Process impact", ApqpService.FlowType, s.Id, $"{s.Number} rev {s.Revision}", $"Step {s.Sequence} {s.Name}", null, "Confirm the process step still matches")).ToList();
        foreach (var l in links.Where(l => l.Type is ApqpService.PfmeaType or ApqpService.PlanType or ApqpService.FlowType))
            process.Add(new ImpactItem("Process impact", l.Type, l.Id, l.Type, l.LinkType, null, "Review the linked APQP document"));
        // Audit impact: obligations mapped to this document, and audits or programs linked to it.
        var obligations = await db.RecordLinks.AsNoTracking().Where(l => l.FromType == ComplianceService.ObligationType && l.ToId == documentId)
            .Join(db.ComplianceObligations, l => l.FromId, o => o.Id, (l, o) => o).ToListAsync(ct);
        var audit = obligations.Select(o => new ImpactItem("Audit impact", ComplianceService.ObligationType, o.Id, o.Number, $"{o.PackCode} {o.Reference} {o.Title}".Trim(), "Requirement satisfied by this document", "Re-check compliance mapping; include in the next audit")).ToList();
        var auditIds = links.Where(l => l.Type is "Audit" or "AuditProgram").ToList();
        audit.AddRange(auditIds.Select(l => new ImpactItem("Audit impact", l.Type, l.Id, l.Type, l.LinkType, null, "Audit against the new revision")));
        var openAudits = await db.Audits.AsNoTracking().Where(a => a.SiteId == doc.SiteId && (a.Status == AuditStatus.Planned || a.Status == AuditStatus.InProgress) && a.Checklist.Any(c => c.Question.Contains(doc.Number))).Select(a => new { a.Id, a.Number, a.Title }).ToListAsync(ct);
        audit.AddRange(openAudits.Select(a => new ImpactItem("Audit impact", "Audit", a.Id, a.Number, a.Title, "Checklist references this document", "Update checklist to the new revision")));
        return new
        {
            Document = new { doc.Id, doc.Number, doc.Title, doc.DocumentType, Kind = doc.Kind.ToString() },
            Areas = new List<ImpactArea> { new("Procedures", procedures), new("Work instructions", wis), new("Forms", forms), new("Training", training), new("Process", process), new("Audits", audit) },
            Summary = new { TrainingImpact = training.Count > 0, ProcessImpact = process.Count > 0, AuditImpact = audit.Count > 0 }
        };
    }

    /// <summary>Evaluates what a change touches. Starts from the change's linked records and affected items, finds the parts and products, then everything controlled for them.</summary>
    public async Task<List<ImpactArea>> ChangeImpactAsync(Guid changeId, CancellationToken ct)
    {
        var c = await m.ViewAsync(ChangeService.RecordType, changeId, ct);
        var links = await LinksAsync(ChangeService.RecordType, changeId, ct);
        var items = await db.ChangeItems.AsNoTracking().Where(i => i.ChangeId == changeId && i.RecordId != null).Select(i => new { i.RecordType, i.RecordId }).ToListAsync(ct);
        var refs = links.Select(l => (l.Type, l.Id)).Concat(items.Select(i => (i.RecordType!, i.RecordId!.Value))).Distinct().ToList();
        var partIds = refs.Where(r => r.Item1 == "Part").Select(r => r.Item2).ToHashSet();
        var productIds = refs.Where(r => r.Item1 == "Product").Select(r => r.Item2).ToHashSet();
        var docIds = refs.Where(r => r.Item1 is "Document" or "Drawing").Select(r => r.Item2).ToList();
        var docPartNumbers = await db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id) && d.PartNumber != null).Select(d => d.PartNumber!).ToListAsync(ct);
        foreach (var p in await db.Parts.AsNoTracking().Where(p => docPartNumbers.Contains(p.PartNumber)).Select(p => p.Id).ToListAsync(ct)) partIds.Add(p);
        foreach (var r in refs.Where(r => r.Item1 is ApqpService.PfmeaType or ApqpService.PlanType or ApqpService.FlowType))
        {
            IQueryable<ApqpDocument> q = r.Item1 == ApqpService.PfmeaType ? db.Pfmeas : r.Item1 == ApqpService.PlanType ? db.ControlPlans : db.ProcessFlows;
            var d = await q.AsNoTracking().Where(x => x.Id == r.Item2).Select(x => new { x.PartId, x.ProductId }).FirstOrDefaultAsync(ct);
            if (d?.PartId is { } pp) partIds.Add(pp);
            if (d?.ProductId is { } pr) productIds.Add(pr);
        }
        foreach (var p in await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id) && p.ProductId != null).Select(p => p.ProductId!.Value).ToListAsync(ct)) productIds.Add(p);
        var parts = await db.Parts.AsNoTracking().Where(p => partIds.Contains(p.Id) || (p.ProductId != null && productIds.Contains(p.ProductId.Value))).ToListAsync(ct);
        var pn = parts.Select(p => p.PartNumber).ToList(); var pids = parts.Select(p => p.Id).ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        var docs = await db.Documents.AsNoTracking().Where(d => !d.IsArchived && d.Status != DocumentStatus.Obsolete && ((d.PartNumber != null && pn.Contains(d.PartNumber)) || docIds.Contains(d.Id))).ToListAsync(ct);
        var allDocIds = docs.Select(d => d.Id).ToList();
        static bool Spec(ControlledDocument d) => d.DocumentType.Contains("Spec", StringComparison.OrdinalIgnoreCase) || d.DocumentType.Contains("Standard", StringComparison.OrdinalIgnoreCase) || d.DocumentType.Contains("Test Method", StringComparison.OrdinalIgnoreCase);
        var pf = await db.Pfmeas.AsNoTracking().Where(x => (x.Status == ApqpStatus.Released || x.Status == ApqpStatus.Draft || x.Status == ApqpStatus.PendingApproval) && ((x.PartId != null && pids.Contains(x.PartId.Value)) || (x.ProductId != null && productIds.Contains(x.ProductId.Value)))).ToListAsync(ct);
        var cp = await db.ControlPlans.AsNoTracking().Where(x => (x.Status == ApqpStatus.Released || x.Status == ApqpStatus.Draft || x.Status == ApqpStatus.PendingApproval) && ((x.PartId != null && pids.Contains(x.PartId.Value)) || (x.ProductId != null && productIds.Contains(x.ProductId.Value)))).ToListAsync(ct);
        var courses = await db.TrainingCourses.AsNoTracking().Where(t => t.DocumentId != null && allDocIds.Contains(t.DocumentId.Value) && !t.IsArchived).ToListAsync(ct);
        var vals = await db.ValidationProjects.AsNoTracking().Where(v => v.Status == ValidationStatus.Released && v.PartId != null && pids.Contains(v.PartId.Value)).ToListAsync(ct);
        var caps = await db.CapabilityStudies.AsNoTracking().Where(s => s.PartId != null && pids.Contains(s.PartId.Value)).ToListAsync(ct);
        return
        [
            new("Products", products.Select(p => new ImpactItem("Product", "Product", p.Id, p.Number, p.Name, null, "Confirm product configuration and customer notification")).Concat(parts.Select(p => new ImpactItem("Product", "Part", p.Id, p.PartNumber, p.Name, "Rev " + p.Revision, "Confirm part revision"))).ToList()),
            new("Drawings", docs.Where(d => d.Kind == DocumentKind.Drawing).Select(d => new ImpactItem("Drawing", "Drawing", d.Id, d.Number, d.Title, d.PartNumber, "Revise drawing")).ToList()),
            new("Specifications", docs.Where(d => d.Kind == DocumentKind.Document && Spec(d)).Select(d => new ImpactItem("Specification", "Document", d.Id, d.Number, d.Title, d.DocumentType, "Revise specification")).ToList()),
            new("FMEA", pf.Select(x => new ImpactItem("FMEA", ApqpService.PfmeaType, x.Id, $"{x.Number} rev {x.Revision}", x.Title, x.Status.ToString(), "Review failure modes and ratings")).ToList()),
            new("Control plans", cp.Select(x => new ImpactItem("Control plan", ApqpService.PlanType, x.Id, $"{x.Number} rev {x.Revision}", x.Title, x.Status.ToString(), "Update characteristics and reaction plans")).ToList()),
            new("Training", courses.Select(t => new ImpactItem("Training", "TrainingCourse", t.Id, t.Number, t.Title, null, "Retrain after release")).ToList()),
            new("Validation", vals.Select(v => new ImpactItem("Validation", ValidationService.RecordType, v.Id, v.Number, v.Title, v.Kind.ToString(), "Assess revalidation")).Concat(caps.Select(s => new ImpactItem("Validation", ProjectService.CapabilityType, s.Id, s.Number, s.Title, $"Cpk {s.Cpk}", "Repeat capability study"))).ToList())
        ];
    }

    /// <summary>Adds every evaluated item to the change's affected items (skipping ones already listed); each must be completed or waived before implementation closes, which drives the approvals.</summary>
    public async Task<int> ApplyChangeImpactAsync(Guid changeId, CancellationToken ct)
    {
        var areas = await ChangeImpactAsync(changeId, ct);
        var existing = await db.ChangeItems.AsNoTracking().Where(i => i.ChangeId == changeId && i.RecordId != null).Select(i => i.RecordId!.Value).ToListAsync(ct);
        var n = 0;
        foreach (var it in areas.SelectMany(a => a.Items).Where(i => !existing.Contains(i.RecordId)).GroupBy(i => i.RecordId).Select(g => g.First()))
        {
            await changes.AddItemAsync(changeId, new ChangeItemInput(it.Category, $"{it.Number} {it.Title}".Trim(), it.RequiredAction, it.RecordType, it.RecordId), ct);
            n++;
        }
        return n;
    }
}
