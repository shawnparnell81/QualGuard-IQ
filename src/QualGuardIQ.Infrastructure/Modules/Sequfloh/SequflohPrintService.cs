using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Sequfloh;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Sequfloh;

/// <summary>
/// Controlled printed layouts for SEQUFLOH (Prompt 1 section 23, Prompt 2 section 20): Validation, Validation / FAI, FAI, Repair Test and Repair Work Instructions,
/// built from the designer schema so every printed section matches the designer. Each print is audited.
/// </summary>
public sealed class SequflohPrintService(AppDbContext db, ModuleSupport m, ICurrentUser current, SequflohInstructionService wi, SequflohFoundationService f)
{
    public async Task<string> OrganizationAsync(CancellationToken ct) => await db.OrganizationSettings.AsNoTracking().Select(s => s.OrganizationName).FirstOrDefaultAsync(ct) ?? "QualGuard IQ";

    public async Task<(string Printed, Func<DateTimeOffset?, string> Local)> ClockAsync(Guid? siteId, CancellationToken ct)
    {
        var zone = await m.ZoneAsync(siteId, ct);
        string Local(DateTimeOffset? t)
        {
            if (t is null) return "";
            var off = zone.GetUtcOffset(t.Value);
            return $"{TimeZoneInfo.ConvertTime(t.Value, zone):yyyy-MM-dd HH:mm} {(off == TimeSpan.Zero ? "UTC" : $"UTC{(off < TimeSpan.Zero ? "-" : "+")}{off:hh\\:mm}")}";
        }
        return (Local(m.Now), Local);
    }

    static string? V(JsonNode? n) => n switch
    {
        null => null,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? "Yes" : "No",
        JsonValue v when v.TryGetValue<decimal>(out var d) => d.ToString("0.######", CultureInfo.InvariantCulture),
        _ => string.IsNullOrWhiteSpace(n.ToString()) ? null : n.ToString()
    };
    static string Words(WorkInstructionStatus s) => System.Text.RegularExpressions.Regex.Replace(s.ToString(), "([a-z])([A-Z])", "$1 $2");

    public async Task<(byte[] Pdf, string FileName)> InstructionAsync(Guid id, CancellationToken ct)
    {
        var x = await wi.LoadAsync(id, false, ct);
        var schema = SequflohDesigner.Schemas[x.Type];
        var settings = await f.GetSettingsAsync(ct);
        var (printed, local) = await ClockAsync(x.SiteId, ct);
        var ids = new[] { x.OwnerId, x.AuthorId, x.ReviewerId, x.ApproverId, x.ReviewedById, x.ApprovedById, x.ReleasedById }.Where(u => u is not null).Select(u => u!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        string? N(Guid? u) => u is { } g && names.TryGetValue(g, out var n) ? n : null;
        var site = await db.Sites.AsNoTracking().Where(s => s.Id == x.SiteId).Select(s => s.Code + " · " + s.Name).FirstOrDefaultAsync(ct);
        var dept = x.DepartmentId is null ? null : await db.Departments.AsNoTracking().Where(d => d.Id == x.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync(ct);
        var content = JsonNode.Parse(x.Content)!.AsObject();
        var blocks = new List<PrintBlock>
        {
            new PrintHeading("Document control"),
            new PrintFields([("Instruction number", x.Number), ("Instruction type", schema.Title), ("Title", x.Title), ("Revision", x.Revision), ("Status", Words(x.Status)),
                ("Effective date", x.EffectiveDate?.ToString("yyyy-MM-dd")), ("Superseded", local(x.SupersededAt)), ("Obsolete", local(x.ObsoleteAt)), ("Site", site), ("Department", dept),
                ("Owner", N(x.OwnerId)), ("Author", N(x.AuthorId)), ("Reviewer", N(x.ReviewerId)), ("Approver", N(x.ApproverId)), ("Created", local(x.CreatedAt)), ("Updated", local(x.UpdatedAt)),
                ("Approved", local(x.ApprovedAt)), ("Released", local(x.ReleasedAt)), ("Revision reason", x.RevisionReason)]),
            new PrintHeading(x.Type == WorkInstructionType.RepairWork ? "Part identification and repair description" : "Applicability"),
            new PrintFields([(x.Type == WorkInstructionType.RepairWork ? "Part being repaired" : "Part number", x.PartNumber), ("Part description", x.PartDescription), ("Product family", x.ProductFamily),
                ("Assembly", x.Assembly), ("Model", x.Model), ("Variant", x.Variant), ("Process", x.Process), ("Operation", x.Operation), ("Workstation", x.Workstation), ("Location", x.Location),
                ("Repair description", x.RepairDescription)]),
        };
        foreach (var s in schema.Sections)
        {
            var src = content[s.Key] as JsonObject;
            var items = s.Fields.Select(fl => (fl.Label, V(src?[fl.Key]))).ToList();
            if (items.All(i => i.Item2 is null) && s.Key is "approval" or "validationApproval" or "faiDisposition") continue;
            blocks.Add(new PrintHeading(s.Title));
            blocks.Add(new PrintFields(items));
        }
        blocks.Add(new PrintHeading(schema.StepsTitle));
        var steps = JsonNode.Parse(x.Steps)!.AsArray();
        if (steps.Count == 0) blocks.Add(new PrintParagraph("No steps."));
        foreach (var st in steps)
        {
            var items = new List<(string, string?)> { ($"Step {V(st?["sequence"])}", V(st?["title"]) ?? "") };
            items.AddRange(schema.StepColumns.Where(c => c.Key != "title").Select(c => (c.Label, V(st?[c.Key]))));
            blocks.Add(new PrintFields(items));
        }
        if (schema.HasCharacteristics)
        {
            blocks.Add(new PrintHeading("FAI characteristics"));
            var rows = JsonNode.Parse(x.Characteristics)!.AsArray().Select(c => new[]
            {
                V(c?["characteristicNumber"]), V(c?["drawingZone"]), V(c?["requirement"]), V(c?["specification"]), V(c?["target"]), V(c?["lowerLimit"]), V(c?["upperLimit"]), V(c?["unit"]),
                string.Join(" ", new[] { V(c?["inspectionMethod"]), V(c?["gaugeOrEquipment"]), V(c?["equipmentId"]) }.Where(s => s is not null)), V(c?["sampleQuantity"]), V(c?["actualResult"]), V(c?["result"])
            }).ToList();
            blocks.Add(new PrintTable([("No.", 0.6), ("Zone", 0.6), ("Requirement", 2.2), ("Specification", 1.4), ("Target", 0.8), ("Lower", 0.8), ("Upper", 0.8), ("Unit", 0.6), ("Method / gauge", 1.6),
                ("Qty", 0.5), ("Actual", 0.9), ("Result", 0.9)], rows));
        }
        if (schema.HasPartsRows)
        {
            blocks.Add(new PrintHeading("Repair parts"));
            var rows = JsonNode.Parse(x.PartsRows)!.AsArray().Select(p => new[]
            {
                V(p?["sequence"]), V(p?["replacementPartNumber"]), V(p?["description"]), V(p?["quantity"]), V(p?["unit"]), V(p?["required"]), V(p?["approvedAlternate"]), V(p?["lotNumberRequired"]),
                V(p?["serialNumberRequired"]), V(p?["issuedQuantity"]), V(p?["usedQuantity"]), V(p?["returnedQuantity"]), V(p?["scrapQuantity"])
            }).ToList();
            blocks.Add(new PrintTable([("Item", 0.5), ("Part number", 1.4), ("Description", 2), ("Qty", 0.6), ("Unit", 0.6), ("Required", 0.8), ("Alternate", 1.1), ("Lot", 0.5), ("Serial", 0.6),
                ("Issued", 0.7), ("Used", 0.6), ("Returned", 0.8), ("Scrap", 0.6)], rows));
        }
        blocks.Add(new PrintHeading("Approvals"));
        blocks.Add(new PrintFields([("Prepared by", N(x.AuthorId)), ("Reviewed by", x.ReviewedAt is null ? null : $"{N(x.ReviewedById)} · {local(x.ReviewedAt)}"),
            ("Approved by", x.ApprovedAt is null ? null : $"{N(x.ApprovedById)} · {local(x.ApprovedAt)}"), ("Released by", x.ReleasedAt is null ? null : $"{N(x.ReleasedById)} · {local(x.ReleasedAt)}"),
            ("Approval statement", x.ApprovedAt is null ? null : settings.WorkInstructions.ApprovalStatement)]));
        blocks.Add(new PrintHeading("Revision history"));
        var revs = await db.SequflohWorkInstructions.AsNoTracking().Where(r => r.FamilyId == x.FamilyId).OrderBy(r => r.CreatedAt)
            .Select(r => new { r.Revision, r.Status, r.EffectiveDate, r.ReleasedAt, r.SupersededAt, r.RevisionReason }).ToListAsync(ct);
        blocks.Add(new PrintTable([("Revision", 0.7), ("Status", 1), ("Effective", 1), ("Released", 1.4), ("Superseded", 1.4), ("Reason", 3)],
            revs.Select(r => new[] { r.Revision, Words(r.Status), r.EffectiveDate?.ToString("yyyy-MM-dd"), local(r.ReleasedAt), local(r.SupersededAt), r.RevisionReason }).ToList()));
        var transitions = await (from t in db.SequflohWorkInstructionTransitions.AsNoTracking() where t.WorkInstructionId == x.Id orderby t.CreatedAt
                                 select new { t.Action, t.FromStatus, t.ToStatus, t.Comment, t.CreatedAt, User = db.Users.Where(u => u.Id == t.UserId).Select(u => u.DisplayName).FirstOrDefault() }).ToListAsync(ct);
        blocks.Add(new PrintTable([("When", 1.3), ("Action", 1.1), ("From", 1), ("To", 1), ("User", 1.2), ("Comment", 2.6)],
            transitions.Select(t => new[] { local(t.CreatedAt), t.Action.Replace('-', ' '), t.FromStatus is null ? "" : Words(t.FromStatus.Value), Words(t.ToStatus), t.User, t.Comment }).ToList()));

        var header = new PrintHeader(await OrganizationAsync(ct), $"{schema.Title}: {x.Title}", x.Number, x.Revision, Words(x.Status), x.EffectiveDate?.ToString("yyyy-MM-dd"),
            x.PartNumber is null ? null : $"Part {x.PartNumber}{(x.PartDescription is null ? "" : " " + x.PartDescription)}", settings.WorkInstructions.PrintFooter, current.UserName, printed,
            x.Status == WorkInstructionStatus.Released ? null : Words(x.Status));
        var pdf = SequflohPdfRenderer.Render(header, blocks);
        f.Audit("WorkInstructionPrinted", SequflohInstructionService.EntityType, x.Id, $"Printed {x.Number} rev {x.Revision} ({Words(x.Status).ToLowerInvariant()})", x.SiteId);
        await db.SaveChangesAsync(ct);
        return (pdf, $"{x.Number}-rev-{x.Revision}.pdf");
    }
}
