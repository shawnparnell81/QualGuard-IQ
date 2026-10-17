using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

public sealed record StartFormRequest(Guid TemplateId, Guid SiteId, Guid? DepartmentId, string? Title);

public sealed class FormService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow)
{
    public const string RecordType = "FormRecord";

    /// <summary>Starts a record on the template's current effective revision; it stays bound to that revision.</summary>
    public async Task<FormRecord> StartAsync(StartFormRequest req, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.FormsEdit, req.SiteId, req.DepartmentId, ct);
        await m.ValidateLocationAsync(req.SiteId, req.DepartmentId, ct);
        var tpl = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == req.TemplateId && d.Kind == DocumentKind.FormTemplate, ct) ?? throw new NotFoundException("Form template");
        var rev = await db.DocumentRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.DocumentId == tpl.Id && r.Status == RevisionStatus.Effective, ct)
            ?? throw new AppException("This form template has no effective revision.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var rec = new FormRecord
        {
            Number = await numbers.NextAsync(RecordType, req.SiteId, ct), TemplateId = tpl.Id, TemplateRevisionId = rev.Id, TemplateNumber = tpl.Number,
            TemplateRevision = rev.Revision, Title = string.IsNullOrWhiteSpace(req.Title) ? tpl.Title : req.Title.Trim(), SiteId = req.SiteId, DepartmentId = req.DepartmentId,
            Status = FormRecordStatus.Draft, ValuesJson = "{}"
        };
        db.FormRecords.Add(rec);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, rec.Id, $"{tpl.Number} rev {rev.Revision}", ct);
        await tx.CommitAsync(ct);
        return rec;
    }

    public async Task<(FormRecord Record, List<FormField> Fields, Dictionary<string, string?> Values)> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var rec = await db.FormRecords.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        var schema = await db.DocumentRevisions.Where(r => r.Id == rec.TemplateRevisionId).Select(r => r.FieldSchema).FirstAsync(ct);
        return (rec, FormSchema.Parse(schema!), JsonSerializer.Deserialize<Dictionary<string, string?>>(rec.ValuesJson) ?? []);
    }

    public async Task<Dictionary<string, string?>> SaveAsync(Guid id, Dictionary<string, string?> values, uint rowVersion, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        var rec = await db.FormRecords.FirstAsync(r => r.Id == id, ct);
        if (rec.Status is not (FormRecordStatus.Draft or FormRecordStatus.Rejected)) throw new AppException("Only draft records can be changed.", 409);
        var fields = await FieldsAsync(rec, ct);
        var normalized = FormSchema.Apply(fields, values, requireComplete: false);
        db.Entry(rec).Property(r => r.RowVersion).OriginalValue = rowVersion;
        rec.ValuesJson = JsonSerializer.Serialize(normalized);
        await db.SaveChangesAsync(ct);
        return normalized;
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        var rec = await db.FormRecords.FirstAsync(r => r.Id == id, ct);
        if (rec.Status is not (FormRecordStatus.Draft or FormRecordStatus.Rejected)) throw new ConflictException("This record was already submitted.");
        var fields = await FieldsAsync(rec, ct);
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(rec.ValuesJson) ?? [];
        rec.ValuesJson = JsonSerializer.Serialize(FormSchema.Apply(fields, values, requireComplete: true));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        rec.Status = FormRecordStatus.Submitted;
        rec.SubmittedAt = m.Now;
        rec.SubmittedById = m.UserId;
        await db.SaveChangesAsync(ct);
        if (await db.ApprovalRoutes.AnyAsync(r => r.RecordType == RecordType && r.IsActive, ct)) await workflow.SubmitAsync(RecordType, id, ct);
        else { rec.Status = FormRecordStatus.Approved; await db.SaveChangesAsync(ct); }
        await m.IndexAsync(RecordType, id, $"{rec.TemplateNumber} rev {rec.TemplateRevision}", ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var rec = await db.FormRecords.FirstAsync(r => r.Id == id, ct);
        rec.Status = FormRecordStatus.Approved;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, id, null, ct);
    }

    public async Task OnRejectedAsync(Guid id, CancellationToken ct)
    {
        var rec = await db.FormRecords.FirstAsync(r => r.Id == id, ct);
        rec.Status = FormRecordStatus.Rejected;
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<FormField>> FieldsAsync(FormRecord rec, CancellationToken ct) =>
        FormSchema.Parse((await db.DocumentRevisions.Where(r => r.Id == rec.TemplateRevisionId).Select(r => r.FieldSchema).FirstAsync(ct))!);
}
