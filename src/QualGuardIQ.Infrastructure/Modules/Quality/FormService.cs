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
public sealed record SignFormRequest(string Password, string? MfaCode, string? Comment);

public sealed class FormService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, SignerVerification signer, FormValueIndexer indexer)
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
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var rec = new FormRecord
        {
            Number = await numbers.NextAsync(RecordType, req.SiteId, ct), TemplateId = tpl.Id, TemplateRevisionId = rev.Id, TemplateNumber = tpl.Number,
            TemplateRevision = rev.Revision, Title = string.IsNullOrWhiteSpace(req.Title) ? tpl.Title : req.Title.Trim(), SiteId = req.SiteId, DepartmentId = req.DepartmentId,
            Status = FormRecordStatus.Draft, ValuesJson = "{}"
        };
        db.FormRecords.Add(rec);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, rec.Id, $"{tpl.Number} rev {rev.Revision}", ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return rec;
    }

    public async Task<(FormRecord Record, FormDefinition Definition, Dictionary<string, string?> Values)> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var rec = await db.FormRecords.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        var schema = await db.DocumentRevisions.Where(r => r.Id == rec.TemplateRevisionId).Select(r => r.FieldSchema).FirstAsync(ct);
        return (rec, FormDefinitions.Parse(schema!), JsonSerializer.Deserialize<Dictionary<string, string?>>(rec.ValuesJson) ?? []);
    }

    public async Task<Dictionary<string, string?>> SaveAsync(Guid id, Dictionary<string, string?> values, uint rowVersion, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        var rec = await db.FormRecords.FirstAsync(r => r.Id == id, ct);
        if (rec.Status is not (FormRecordStatus.Draft or FormRecordStatus.Rejected)) throw new AppException("Only draft records can be changed.", 409);
        var fields = await FieldsAsync(rec, ct);
        var normalized = FormDefinitions.Apply(fields, values, requireComplete: false);
        db.Entry(rec).Property(r => r.RowVersion).OriginalValue = rowVersion;
        var json = JsonSerializer.Serialize(normalized);
        if (!SameValues(json, rec.ValuesJson))
        {
            // signatures are bound to the content they signed: any change invalidates them (kept as history)
            foreach (var sig in await db.FormSignatures.Where(s => s.FormRecordId == id && !s.IsInvalidated).ToListAsync(ct))
            { sig.IsInvalidated = true; sig.InvalidatedAt = m.Now; sig.InvalidatedReason = "The record was changed after it was signed."; }
        }
        rec.ValuesJson = json;
        await db.SaveChangesAsync(ct);
        await indexer.IndexAsync(rec, normalized, ct);
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
        var json = JsonSerializer.Serialize(FormDefinitions.Apply(fields, values, requireComplete: true));
        if (!SameValues(json, rec.ValuesJson)) throw new AppException("Save the record before submitting it.", 409);
        var signedKeys = await db.FormSignatures.Where(s => s.FormRecordId == id && !s.IsInvalidated).Select(s => s.SignatureKey).ToListAsync(ct);
        var missing = fields.Signatures.Where(b => b.Required && PreSubmitRoles.Contains(b.Role) && !signedKeys.Contains(b.Key)).Select(b => b.Label).ToList();
        if (missing.Count > 0) throw new AppException($"Sign before submitting: {string.Join(", ", missing)}.");
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

    /// <summary>Roles signed while the record is being completed (before submission). Reviewer, approver, customer and supplier signatures can follow submission.</summary>
    public static readonly IReadOnlySet<string> PreSubmitRoles = new HashSet<string> { "prepared", "performed", "verified", "witness" };

    public async Task<List<FormSignature>> SignaturesAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        return await db.FormSignatures.AsNoTracking().Where(s => s.FormRecordId == id).OrderBy(s => s.SignedAt).ToListAsync(ct);
    }

    /// <summary>Applies an electronic signature to a signature block after re-authentication. The signature is bound to the current content.</summary>
    public async Task<FormSignature> SignAsync(Guid id, string key, SignFormRequest req, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var rec = await db.FormRecords.FirstAsync(r => r.Id == id, ct);
        if (rec.Status == FormRecordStatus.Approved) throw new ConflictException("An approved record cannot be signed.");
        var def = await FieldsAsync(rec, ct);
        var block = def.Signatures.FirstOrDefault(b => b.Key == key) ?? throw new NotFoundException("Signature block");
        var preSubmit = PreSubmitRoles.Contains(block.Role);
        if (preSubmit && rec.Status == FormRecordStatus.Submitted) throw new ConflictException($"'{block.Label}' is signed before the record is submitted.");
        var uid = m.UserId;
        var permission = block.RequiredPermission ?? (preSubmit ? Permissions.FormsEdit : Permissions.FormsApprove);
        if (!Permissions.All.Contains(permission)) throw new AppException($"Signature '{block.Label}' needs unknown permission '{permission}'.");
        if (!await m.CanAsync(permission, rec.SiteId, rec.DepartmentId, ct)) throw new ForbiddenException($"You do not hold the permission needed to sign '{block.Label}'.");
        var valid = await db.FormSignatures.Where(s => s.FormRecordId == id && !s.IsInvalidated).ToListAsync(ct);
        if (valid.Any(s => s.SignatureKey == key)) throw new ConflictException($"'{block.Label}' is already signed.");
        var distinct = (block.DistinctFrom ?? []).Concat(def.Signatures.Where(b => b.DistinctFrom?.Contains(key) == true).Select(b => b.Key)).ToHashSet();
        if (valid.Any(s => distinct.Contains(s.SignatureKey) && s.SignerId == uid)) throw new ForbiddenException($"'{block.Label}' must be signed by a different person.");
        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(rec.ValuesJson) ?? [];
        var normalized = FormDefinitions.Apply(def, values, requireComplete: preSubmit);
        if (!SameValues(JsonSerializer.Serialize(normalized), rec.ValuesJson)) throw new AppException("Save the record before signing it.", 409);
        if (req.Comment is { Length: > 1000 }) throw new AppException("The comment can be at most 1000 characters.");
        var (user, verification) = await signer.VerifyAsync(uid, req.Password, req.MfaCode, false, $"Form signature {rec.Number}", "Set up an authenticator app first.");
        var sig = new FormSignature
        {
            FormRecordId = id, SignatureKey = key, Label = block.Label, Role = block.Role, Meaning = block.Meaning, SignerId = uid, SignerName = user.DisplayName,
            SignedAt = m.Now, Verification = verification, Comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim(), ContentHash = Sha(JsonSerializer.Serialize(normalized)),
        };
        sig.SignatureHash = SignatureHash(sig, rec.Number);
        db.FormSignatures.Add(sig);
        await db.SaveChangesAsync(ct);
        return sig;
    }

    /// <summary>Compares two stored value sets by content; the database may reorder keys and whitespace.</summary>
    public static bool SameValues(string a, string b)
    {
        var x = JsonSerializer.Deserialize<Dictionary<string, string?>>(a) ?? [];
        var y = JsonSerializer.Deserialize<Dictionary<string, string?>>(b) ?? [];
        return x.Count == y.Count && x.All(kv => y.TryGetValue(kv.Key, out var v) && v == kv.Value);
    }

    public static string Sha(string s) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    public static string SignatureHash(FormSignature s, string recordNumber) =>
        Sha($"{s.FormRecordId}|{s.SignatureKey}|{s.SignerId}|{s.SignerName}|{s.Meaning}|{s.SignedAt.UtcTicks / 10}|{s.ContentHash}|{recordNumber}|{s.Comment}");

    private async Task<FormDefinition> FieldsAsync(FormRecord rec, CancellationToken ct) =>
        FormDefinitions.Parse((await db.DocumentRevisions.Where(r => r.Id == rec.TemplateRevisionId).Select(r => r.FieldSchema).FirstAsync(ct))!);
}
