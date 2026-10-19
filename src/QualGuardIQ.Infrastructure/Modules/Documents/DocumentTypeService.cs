using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Documents;

public sealed record DocumentTypeInput(DocumentKind Kind, string Name, string? Description, int SortOrder = 0, bool IsReference = false);
public sealed record DocumentTypeUpdate(string Name, string? Description, bool IsActive, int SortOrder, bool? IsReference = null);

/// <summary>Governed document type library: idempotent seeding, rename (carried to documents of that type), deactivation, and the create-time check.</summary>
public sealed class DocumentTypeService(AppDbContext db)
{
    /// <summary>Seed library. Keys are stable; names are the shipped defaults and may be renamed by the customer afterwards.</summary>
    public static readonly (DocumentKind Kind, string Key, string Name)[] Seed =
    [
        (DocumentKind.Document, "quality-manual", "Quality Manual"),
        (DocumentKind.Document, "policy", "Policy"),
        (DocumentKind.Document, "sop", "Standard Operating Procedure"),
        (DocumentKind.Document, "procedure", "Procedure"),
        (DocumentKind.Document, "work-instruction", "Work Instruction"),
        (DocumentKind.Document, "form", "Form"),
        (DocumentKind.Document, "engineering-drawing", "Engineering Drawing"),
        (DocumentKind.Document, "specification", "Specification"),
        (DocumentKind.Document, "inspection-instruction", "Inspection Instruction"),
        (DocumentKind.Document, "test-method", "Test Method"),
        (DocumentKind.Document, "control-plan", "Control Plan"),
        (DocumentKind.Document, "process-flow", "Process Flow"),
        (DocumentKind.Document, "training-material", "Training Material"),
        (DocumentKind.Document, "customer-document", "Customer Document"),
        (DocumentKind.Document, "supplier-document", "Supplier Document"),
        (DocumentKind.Document, "external-standard", "External Standard"),
        (DocumentKind.Document, "certificate", "Certificate"),
        (DocumentKind.Document, "technical-data", "Technical Data"),
        (DocumentKind.Document, "maintenance-instruction", "Maintenance Instruction"),
        (DocumentKind.Document, "calibration-instruction", "Calibration Instruction"),
        (DocumentKind.Document, "packaging-instruction", "Packaging Instruction"),
        (DocumentKind.Document, "labeling-instruction", "Labeling Instruction"),
        (DocumentKind.Document, "video", "Video"),
        (DocumentKind.Document, "reference-document", "Reference Document"),
        // Types the screens offered before the library existed (kept so existing documents stay on a listed type).
        (DocumentKind.Document, "manual", "Manual"),
        (DocumentKind.Document, "standard", "Standard"),
        (DocumentKind.Document, "plan", "Plan"),
        (DocumentKind.Drawing, "assembly-drawing", "Assembly Drawing"),
        (DocumentKind.Drawing, "detail-drawing", "Detail Drawing"),
        (DocumentKind.Drawing, "layout", "Layout"),
        (DocumentKind.Drawing, "schematic", "Schematic"),
        (DocumentKind.Drawing, "scd", "Specification Control Drawing"),
        (DocumentKind.FormTemplate, "inspection-record", "Inspection Record"),
        (DocumentKind.FormTemplate, "checklist", "Checklist"),
        (DocumentKind.FormTemplate, "log", "Log"),
        (DocumentKind.FormTemplate, "report", "Report"),
        (DocumentKind.FormTemplate, "request", "Request"),
    ];

    /// <summary>Seeded types whose documents are reference copies (no approval or revision workflow).</summary>
    public static readonly HashSet<string> ReferenceKeys = ["customer-document", "supplier-document", "external-standard", "reference-document", "standard"];

    /// <summary>
    /// Idempotent: a seed row already present by key is left alone (keeps renames and deactivations); a customer type with the
    /// same name is adopted (key attached, name and state kept); types used by existing documents but missing from the
    /// library are added as customer types. Returns the number of rows added.
    /// </summary>
    public async Task<int> EnsureSeededAsync(CancellationToken ct)
    {
        var existing = await db.DocumentTypes.ToListAsync(ct);
        var added = 0;
        for (var i = 0; i < Seed.Length; i++)
        {
            var (kind, key, name) = Seed[i];
            if (existing.Any(x => x.Kind == kind && x.SeedKey == key)) continue;
            var same = existing.FirstOrDefault(x => x.Kind == kind && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (same is not null) { if (same.SeedKey is null) { same.SeedKey = key; same.IsSystem = true; } continue; }
            var t = new DocumentTypeDefinition { Kind = kind, Name = name, SeedKey = key, IsSystem = true, IsActive = true, SortOrder = (i + 1) * 10, IsReference = kind == DocumentKind.Document && ReferenceKeys.Contains(key) };
            db.DocumentTypes.Add(t); existing.Add(t); added++;
        }
        var used = await db.Documents.AsNoTracking().Select(d => new { d.Kind, d.DocumentType }).Distinct().ToListAsync(ct);
        foreach (var u in used.Where(u => !string.IsNullOrWhiteSpace(u.DocumentType)))
        {
            if (existing.Any(x => x.Kind == u.Kind && string.Equals(x.Name, u.DocumentType, StringComparison.OrdinalIgnoreCase))) continue;
            var t = new DocumentTypeDefinition { Kind = u.Kind, Name = u.DocumentType.Trim(), IsSystem = false, IsActive = true, SortOrder = 1000 };
            db.DocumentTypes.Add(t); existing.Add(t); added++;
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task<List<DocumentTypeDefinition>> ListAsync(DocumentKind? kind, bool includeInactive, CancellationToken ct) =>
        await db.DocumentTypes.AsNoTracking().Where(x => (kind == null || x.Kind == kind) && (includeInactive || x.IsActive)).OrderBy(x => x.Kind).ThenBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);

    public async Task<DocumentTypeDefinition> AddAsync(DocumentTypeInput r, CancellationToken ct)
    {
        var name = Clean(r.Name);
        if (await db.DocumentTypes.AnyAsync(x => x.Kind == r.Kind && x.Name.ToLower() == name.ToLower(), ct)) throw new ConflictException($"A {r.Kind} type named '{name}' already exists.");
        if (r.IsReference && r.Kind != DocumentKind.Document) throw new AppException("Only document types can be reference types.");
        var t = new DocumentTypeDefinition { Kind = r.Kind, Name = name, Description = r.Description?.Trim(), SortOrder = r.SortOrder == 0 ? 1000 : r.SortOrder, IsReference = r.IsReference };
        db.DocumentTypes.Add(t);
        await db.SaveChangesAsync(ct);
        return t;
    }

    /// <summary>Rename and (de)activate. A rename is carried to documents of that kind and type in the same transaction (each change audited).</summary>
    public async Task<DocumentTypeDefinition> UpdateAsync(Guid id, DocumentTypeUpdate r, CancellationToken ct)
    {
        var t = await db.DocumentTypes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Document type");
        var name = Clean(r.Name);
        if (!string.Equals(name, t.Name, StringComparison.Ordinal) && await db.DocumentTypes.AnyAsync(x => x.Id != id && x.Kind == t.Kind && x.Name.ToLower() == name.ToLower(), ct))
            throw new ConflictException($"A {t.Kind} type named '{name}' already exists.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!string.Equals(name, t.Name, StringComparison.Ordinal))
        {
            var old = t.Name;
            foreach (var d in await db.Documents.Where(d => d.Kind == t.Kind && d.DocumentType == old).ToListAsync(ct)) d.DocumentType = name;
            t.Name = name;
        }
        t.Description = r.Description?.Trim(); t.IsActive = r.IsActive; t.SortOrder = r.SortOrder;
        // documents already created keep how they were created; the setting applies to new documents of the type
        if (r.IsReference is { } isRef)
        {
            if (isRef && t.Kind != DocumentKind.Document) throw new AppException("Only document types can be reference types.");
            t.IsReference = isRef;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return t;
    }

    public async Task<bool> IsReferenceAsync(DocumentKind kind, string name, CancellationToken ct) =>
        kind == DocumentKind.Document && await db.DocumentTypes.AsNoTracking().AnyAsync(x => x.Kind == kind && x.Name.ToLower() == name.ToLower() && x.IsReference, ct);

    /// <summary>New documents must use an active library type; returns its canonical spelling. Existing documents may keep a type that was later deactivated.</summary>
    public async Task<string> RequireActiveAsync(DocumentKind kind, string name, string? current, CancellationToken ct)
    {
        var n = (name ?? "").Trim();
        if (current is not null && string.Equals(n, current, StringComparison.OrdinalIgnoreCase)) return current;
        var t = await db.DocumentTypes.AsNoTracking().FirstOrDefaultAsync(x => x.Kind == kind && x.Name.ToLower() == n.ToLower(), ct);
        if (t is null) throw new AppException($"'{n}' is not in the document type library. Choose a listed type or ask an administrator to add it.");
        if (!t.IsActive) throw new AppException($"The document type '{t.Name}' is no longer in use.");
        return t.Name;
    }

    private static string Clean(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length is 0 or > 60) throw new AppException("A document type name is 1 to 60 characters.");
        return n;
    }
}
