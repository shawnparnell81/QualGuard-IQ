using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Forms;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

public sealed record FormComponentInput(string Key, string Name, string? Category, string? Description, string FieldsJson);
public sealed record FormPreviewInput(string FieldsJson, Dictionary<string, string?>? Values);
public sealed record FormPreviewResult(bool Valid, List<string> Visible, Dictionary<string, string?>? Values, string? Error);

/// <summary>
/// Form designer platform on the existing controlled-forms engine: a versioned library of reusable components, dynamic rules and validation
/// (evaluated by FormSchema, the same code that validates records), and live preview. Templates themselves stay document-controlled revisions.
/// </summary>
public sealed class FormLibraryService(AppDbContext db, ModuleSupport m)
{
    async Task RequireAnyAsync(string permission, CancellationToken ct) { if (!await m.CanAnyAsync(permission, ct)) throw new ForbiddenException(); }

    public async Task<List<FormComponent>> ListAsync(CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsView, ct);
        return await db.FormComponents.AsNoTracking().Where(c => c.Status != LibraryStatus.Retired).OrderBy(c => c.Category).ThenBy(c => c.Name).ThenByDescending(c => c.Version).ToListAsync(ct);
    }

    static void Check(FormComponentInput r)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(r.Key ?? "", "^[a-z][a-z0-9_]{1,30}$")) throw new AppException("Component key must be 2–31 lowercase letters, numbers or underscores.");
        ModuleSupport.Required(r.Name, "Name", 200);
        FormSchema.Parse(r.FieldsJson);
    }

    public async Task<FormComponent> CreateAsync(FormComponentInput r, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsEdit, ct);
        Check(r);
        if (await db.FormComponents.AnyAsync(c => c.Key == r.Key, ct)) throw new ConflictException("A component with this key already exists. Create a new version instead.");
        var c = new FormComponent { Key = r.Key, Name = r.Name.Trim(), Category = string.IsNullOrWhiteSpace(r.Category) ? "General" : r.Category.Trim(), Description = r.Description?.Trim(), FieldsJson = FormSchema.Serialize(FormSchema.Parse(r.FieldsJson)) };
        db.FormComponents.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task UpdateAsync(Guid id, FormComponentInput r, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsEdit, ct);
        var c = await db.FormComponents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Form component");
        if (c.Status != LibraryStatus.Draft) throw new ConflictException("Published components cannot be changed. Create a new version.");
        Check(r with { Key = c.Key });
        c.Name = r.Name.Trim(); c.Category = string.IsNullOrWhiteSpace(r.Category) ? "General" : r.Category.Trim(); c.Description = r.Description?.Trim(); c.FieldsJson = FormSchema.Serialize(FormSchema.Parse(r.FieldsJson));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Publishes a draft (forms.approve). The previous published version is retired; templates that copied it keep their copy.</summary>
    public async Task PublishAsync(Guid id, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsApprove, ct);
        var c = await db.FormComponents.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Form component");
        if (c.Status != LibraryStatus.Draft) throw new ConflictException("Only drafts can be published.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var old in await db.FormComponents.Where(x => x.Key == c.Key && x.Status == LibraryStatus.Published).ToListAsync(ct)) old.Status = LibraryStatus.Retired;
        await db.SaveChangesAsync(ct);
        c.Status = LibraryStatus.Published; c.PublishedAt = m.Now; c.PublishedById = m.UserId;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<FormComponent> NewVersionAsync(Guid id, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsEdit, ct);
        var c = await db.FormComponents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Form component");
        if (await db.FormComponents.AnyAsync(x => x.Key == c.Key && x.Status == LibraryStatus.Draft, ct)) throw new ConflictException("A draft version already exists.");
        var max = await db.FormComponents.Where(x => x.Key == c.Key).MaxAsync(x => x.Version, ct);
        var n = new FormComponent { Key = c.Key, Name = c.Name, Category = c.Category, Description = c.Description, FieldsJson = c.FieldsJson, Version = max + 1 };
        db.FormComponents.Add(n);
        await db.SaveChangesAsync(ct);
        return n;
    }

    /// <summary>Fields of a published component ready to insert into a template: keys get the prefix, internal references follow, and each field records its source version.</summary>
    public async Task<List<FormField>> InsertAsync(Guid id, string? prefix, CancellationToken ct)
    {
        await RequireAnyAsync(Permissions.FormsView, ct);
        var c = await db.FormComponents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.Status == LibraryStatus.Published, ct) ?? throw new NotFoundException("Published form component");
        var p = string.IsNullOrWhiteSpace(prefix) ? "" : prefix.Trim().ToLowerInvariant().TrimEnd('_') + "_";
        if (p.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(p, "^[a-z][a-z0-9_]{0,20}$")) throw new AppException("The prefix must be lowercase letters, numbers or underscores.");
        var fields = FormSchema.Parse(c.FieldsJson);
        var keys = fields.Select(f => f.Key).ToHashSet();
        string K(string k) => keys.Contains(k) ? p + k : k;
        foreach (var f in fields)
        {
            f.Key = p + f.Key; f.FromComponent = $"{c.Key}@{c.Version}";
            if (f.VisibleWhen is { } v) v.Field = K(v.Field);
            if (f.RequiredWhen is { } rq) rq.Field = K(rq.Field);
            foreach (var ch in f.Checks ?? []) if (!string.IsNullOrWhiteSpace(ch.Field)) ch.Field = K(ch.Field);
            if (f.Formula is { } fm) f.Formula = System.Text.RegularExpressions.Regex.Replace(fm, "[A-Za-z][A-Za-z0-9_]*", mt => K(mt.Value));
            if (p.Length > 0 && f.Key.Length > 50) throw new AppException("The prefix makes a field key longer than 50 characters.");
        }
        return fields;
    }

    /// <summary>Live preview: validates the design, applies dynamic rules to sample values and reports what a person filling the form would see.</summary>
    public static FormPreviewResult Preview(FormPreviewInput r)
    {
        List<FormField> fields;
        try { fields = FormSchema.Parse(r.FieldsJson); } catch (AppException e) { return new FormPreviewResult(false, [], null, e.Message); }
        var values = r.Values ?? [];
        var visible = FormSchema.VisibleKeys(fields, values).ToList();
        try { return new FormPreviewResult(true, visible, FormSchema.Apply(fields, values, requireComplete: true), null); }
        catch (AppException e) { return new FormPreviewResult(true, visible, null, e.Message); }
    }
}
