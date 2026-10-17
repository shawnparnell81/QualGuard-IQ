using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.MasterData;

public sealed record SaveProductRequest(string Number, string Name, string? Description, Guid? FamilyId, string? Category, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, MasterStatus Status, uint RowVersion = 0);
public sealed record SavePartRequest(string PartNumber, string Name, string? Description, PartType PartType, string Revision, string UnitOfMeasure, Guid? ProductId,
    string? Material, bool HasSpecialCharacteristics, Guid SiteId, Guid? DepartmentId, MasterStatus Status, uint RowVersion = 0);
public sealed record RevisePartRequest(string Revision, string Reason, DateOnly? EffectiveOn);
public sealed record SaveCustomerRequest(string Code, string Name, string? ContactName, string? Email, string? Phone, string? Address, Guid SiteId, Guid? DepartmentId, MasterStatus Status, uint RowVersion = 0);

/// <summary>Products, parts and customers: shared master records referenced by every module.</summary>
public sealed class MasterDataService(AppDbContext db, ModuleSupport m)
{
    public const string ProductType = "Product";
    public const string PartRecordType = "Part";
    public const string CustomerType = "Customer";

    private static string Code(string? v, string label, int max)
    {
        ModuleSupport.Required(v, label, max);
        return v!.Trim();
    }

    private static string? Opt(string? v, string label, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        if (v.Length > max) throw new AppException($"{label} is limited to {max} characters.");
        return v.Trim();
    }

    public async Task<Product> SaveProductAsync(Guid? id, SaveProductRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.MasterDataEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var number = Code(r.Number, "Product number", 60);
        if (await db.Products.AnyAsync(p => p.Number == number && p.Id != id, ct)) throw new ConflictException("A product with this number already exists.");
        if (r.FamilyId is { } f && !await db.ProductFamilies.AnyAsync(x => x.Id == f && !x.IsArchived, ct)) throw new AppException("Select an active product family.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        Product p;
        if (id is null) { p = new Product(); db.Products.Add(p); }
        else
        {
            await m.EditAsync(ProductType, id.Value, ct);
            p = await db.Products.FirstAsync(x => x.Id == id, ct);
            db.Entry(p).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        }
        p.Number = number; p.Name = Code(r.Name, "Name", 200); p.Description = Opt(r.Description, "Description", 4000);
        p.FamilyId = r.FamilyId; p.Category = Opt(r.Category, "Category", 100); p.SiteId = r.SiteId; p.DepartmentId = r.DepartmentId; p.OwnerId = r.OwnerId; p.Status = r.Status;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ProductType, p.Id, $"{p.Description} {p.Category}", ct);
        return p;
    }

    public async Task<Part> SavePartAsync(Guid? id, SavePartRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.MasterDataEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var number = Code(r.PartNumber, "Part number", 60);
        if (await db.Parts.AnyAsync(p => p.PartNumber == number && p.Id != id, ct)) throw new ConflictException("A part with this number already exists.");
        if (r.ProductId is { } pid && !await db.Products.AnyAsync(x => x.Id == pid, ct)) throw new AppException("The product does not exist.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        Part p;
        if (id is null)
        {
            p = new Part { Revision = Code(r.Revision, "Revision", 10) };
            db.Parts.Add(p);
            db.PartRevisions.Add(new PartRevision { PartId = p.Id, Revision = p.Revision, Reason = "Initial release", EffectiveOn = await m.TodayAsync(r.SiteId, ct) });
        }
        else
        {
            await m.EditAsync(PartRecordType, id.Value, ct);
            p = await db.Parts.FirstAsync(x => x.Id == id, ct);
            db.Entry(p).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
            if (!string.Equals(r.Revision?.Trim(), p.Revision, StringComparison.Ordinal)) throw new AppException("Use Revise to change the part revision; history is kept.");
        }
        p.PartNumber = number; p.Name = Code(r.Name, "Name", 200); p.Description = Opt(r.Description, "Description", 4000); p.PartType = r.PartType;
        p.UnitOfMeasure = Code(r.UnitOfMeasure, "Unit of measure", 20); p.ProductId = r.ProductId; p.Material = Opt(r.Material, "Material", 200);
        p.HasSpecialCharacteristics = r.HasSpecialCharacteristics; p.SiteId = r.SiteId; p.DepartmentId = r.DepartmentId; p.Status = r.Status;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(PartRecordType, p.Id, $"{p.Description} {p.Material}", ct);
        await tx.CommitAsync(ct);
        return p;
    }

    public async Task RevisePartAsync(Guid id, RevisePartRequest r, CancellationToken ct)
    {
        await m.EditAsync(PartRecordType, id, ct);
        var p = await db.Parts.FirstAsync(x => x.Id == id, ct);
        var rev = Code(r.Revision, "Revision", 10);
        ModuleSupport.Required(r.Reason, "Reason", 1000);
        if (await db.PartRevisions.AnyAsync(x => x.PartId == id && x.Revision == rev, ct)) throw new ConflictException("That revision already exists for this part.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.PartRevisions.Add(new PartRevision { PartId = id, Revision = rev, Reason = r.Reason.Trim(), EffectiveOn = r.EffectiveOn ?? await m.TodayAsync(p.SiteId, ct) });
        p.Revision = rev;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(PartRecordType, p.Id, p.Description, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<Customer> SaveCustomerAsync(Guid? id, SaveCustomerRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.MasterDataEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var code = Code(r.Code, "Customer code", 40);
        if (await db.Customers.AnyAsync(c => c.Code == code && c.Id != id, ct)) throw new ConflictException("A customer with this code already exists.");
        var email = Opt(r.Email, "Email", 256);
        if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _)) throw new AppException("Enter a valid email address.");
        Customer c;
        if (id is null) { c = new Customer(); db.Customers.Add(c); }
        else
        {
            await m.EditAsync(CustomerType, id.Value, ct);
            c = await db.Customers.FirstAsync(x => x.Id == id, ct);
            db.Entry(c).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        }
        c.Code = code; c.Name = Code(r.Name, "Name", 200); c.ContactName = Opt(r.ContactName, "Contact", 200); c.Email = email;
        c.Phone = Opt(r.Phone, "Phone", 40); c.Address = Opt(r.Address, "Address", 500); c.SiteId = r.SiteId; c.DepartmentId = r.DepartmentId; c.Status = r.Status;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(CustomerType, c.Id, $"{c.ContactName} {c.Email}", ct);
        return c;
    }

    public async Task<ProductFamily> CreateFamilyAsync(string code, string name, CancellationToken ct, string? regulatoryCategory = null)
    {
        var c = Code(code, "Code", 40);
        if (await db.ProductFamilies.AnyAsync(f => f.Code == c, ct)) throw new ConflictException("That family code already exists.");
        var f = new ProductFamily { Code = c, Name = Code(name, "Name", 200), RegulatoryCategory = Opt(regulatoryCategory, "Regulatory category", 100) };
        db.ProductFamilies.Add(f);
        await db.SaveChangesAsync(ct);
        return f;
    }

    public async Task UpdateFamilyAsync(Guid id, string name, string? regulatoryCategory, CancellationToken ct)
    {
        var f = await db.ProductFamilies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Product family");
        f.Name = Code(name, "Name", 200); f.RegulatoryCategory = Opt(regulatoryCategory, "Regulatory category", 100);
        await db.SaveChangesAsync(ct);
    }
}
