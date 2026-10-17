using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Api.Auth;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.MasterData;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Api.Controllers;

[ApiController, Authorize, Route("api/master")]
public sealed class MasterDataController(AppDbContext db, MasterDataService svc, ModuleSupport m) : ControllerBase
{
    public sealed record FamilyRequest(string Code, string Name);

    [HttpGet("families")]
    [RequirePermission(Permissions.MasterDataView)]
    public async Task<object> Families(CancellationToken ct) =>
        await db.ProductFamilies.AsNoTracking().Where(f => !f.IsArchived).OrderBy(f => f.Code).Select(f => new { f.Id, f.Code, f.Name }).ToListAsync(ct);

    [HttpPost("families")]
    [RequirePermission(Permissions.MasterDataEdit)]
    public async Task<object> CreateFamily(FamilyRequest r, CancellationToken ct) => new { (await svc.CreateFamilyAsync(r.Code, r.Name, ct)).Id };

    [HttpGet("products")]
    [RequirePermission(Permissions.MasterDataView)]
    public async Task<object> Products([FromQuery] string? q, [FromQuery] MasterStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Products.AsNoTracking(), Permissions.MasterDataView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number + " " + x.Name + " " + (x.Category ?? ""), $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderBy(x => x.Number).Select(x => new
        {
            x.Id, x.Number, x.Name, x.Category, x.Status,
            Family = db.ProductFamilies.Where(f => f.Id == x.FamilyId).Select(f => f.Name).FirstOrDefault(),
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First(),
            Parts = db.Parts.Count(p => p.ProductId == x.Id)
        }), page, pageSize, ct);
    }

    [HttpPost("products")]
    [RequirePermission(Permissions.MasterDataEdit)]
    public async Task<object> CreateProduct(SaveProductRequest r, CancellationToken ct) => new { (await svc.SaveProductAsync(null, r, ct)).Id };

    [HttpPut("products/{id:guid}")]
    public async Task<IActionResult> UpdateProduct(Guid id, SaveProductRequest r, CancellationToken ct) { await svc.SaveProductAsync(id, r, ct); return NoContent(); }

    [HttpGet("products/{id:guid}")]
    public async Task<object> Product(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(MasterDataService.ProductType, id, ct);
        var p = await db.Products.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var ncrScope = await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        return new
        {
            p.Id, p.Number, p.Name, p.Description, p.FamilyId, p.Category, p.SiteId, p.DepartmentId, p.OwnerId, p.Status, p.RowVersion, p.CreatedAt,
            OwnerName = p.OwnerId == null ? null : await db.Users.Where(u => u.Id == p.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            Parts = await db.Parts.Where(x => x.ProductId == id).OrderBy(x => x.PartNumber).Select(x => new { x.Id, x.PartNumber, x.Name, x.Revision, x.PartType, x.Status }).ToListAsync(ct),
            Ncrs = ncrScope is null ? null : await ncrScope.Where(n => n.ProductId == id).OrderByDescending(n => n.CreatedAt).Take(20).Select(n => new { n.Id, n.Number, n.Title, n.Status }).ToListAsync(ct),
            CanEdit = await m.CanAsync(Permissions.MasterDataEdit, p.SiteId, p.DepartmentId, ct)
        };
    }

    [HttpGet("parts")]
    [RequirePermission(Permissions.MasterDataView)]
    public async Task<object> Parts([FromQuery] string? q, [FromQuery] Guid? productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Parts.AsNoTracking(), Permissions.MasterDataView, ct);
        if (productId is not null) query = query.Where(x => x.ProductId == productId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.PartNumber + " " + x.Name + " " + (x.Material ?? ""), $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderBy(x => x.PartNumber).Select(x => new
        {
            x.Id, x.PartNumber, x.Name, x.Revision, x.PartType, x.UnitOfMeasure, x.Status, x.HasSpecialCharacteristics,
            Product = db.Products.Where(p => p.Id == x.ProductId).Select(p => p.Number).FirstOrDefault(),
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First()
        }), page, pageSize, ct);
    }

    [HttpPost("parts")]
    [RequirePermission(Permissions.MasterDataEdit)]
    public async Task<object> CreatePart(SavePartRequest r, CancellationToken ct) => new { (await svc.SavePartAsync(null, r, ct)).Id };

    [HttpPut("parts/{id:guid}")]
    public async Task<IActionResult> UpdatePart(Guid id, SavePartRequest r, CancellationToken ct) { await svc.SavePartAsync(id, r, ct); return NoContent(); }

    [HttpPost("parts/{id:guid}/revise")]
    public async Task<IActionResult> RevisePart(Guid id, RevisePartRequest r, CancellationToken ct) { await svc.RevisePartAsync(id, r, ct); return NoContent(); }

    [HttpGet("parts/{id:guid}")]
    public async Task<object> Part(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(MasterDataService.PartRecordType, id, ct);
        var p = await db.Parts.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return new
        {
            p.Id, p.PartNumber, p.Name, p.Description, p.PartType, p.Revision, p.UnitOfMeasure, p.ProductId, p.Material, p.HasSpecialCharacteristics,
            p.SiteId, p.DepartmentId, p.Status, p.RowVersion, p.CreatedAt,
            Product = p.ProductId == null ? null : await db.Products.Where(x => x.Id == p.ProductId).Select(x => new { x.Id, x.Number, x.Name }).FirstAsync(ct),
            Revisions = await db.PartRevisions.Where(r => r.PartId == id).OrderByDescending(r => r.CreatedAt).Select(r => new { r.Id, r.Revision, r.Reason, r.EffectiveOn, r.CreatedAt }).ToListAsync(ct),
            CanEdit = await m.CanAsync(Permissions.MasterDataEdit, p.SiteId, p.DepartmentId, ct)
        };
    }

    [HttpGet("customers")]
    [RequirePermission(Permissions.MasterDataView)]
    public async Task<object> Customers([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Customers.AsNoTracking(), Permissions.MasterDataView, ct);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Code + " " + x.Name + " " + (x.ContactName ?? ""), $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderBy(x => x.Code).Select(x => new
        {
            x.Id, x.Code, x.Name, x.ContactName, x.Email, x.Phone, x.Status,
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First()
        }), page, pageSize, ct);
    }

    [HttpPost("customers")]
    [RequirePermission(Permissions.MasterDataEdit)]
    public async Task<object> CreateCustomer(SaveCustomerRequest r, CancellationToken ct) => new { (await svc.SaveCustomerAsync(null, r, ct)).Id };

    [HttpPut("customers/{id:guid}")]
    public async Task<IActionResult> UpdateCustomer(Guid id, SaveCustomerRequest r, CancellationToken ct) { await svc.SaveCustomerAsync(id, r, ct); return NoContent(); }

    [HttpGet("customers/{id:guid}")]
    public async Task<object> Customer(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(MasterDataService.CustomerType, id, ct);
        var c = await db.Customers.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return new
        {
            c.Id, c.Code, c.Name, c.ContactName, c.Email, c.Phone, c.Address, c.SiteId, c.DepartmentId, c.Status, c.RowVersion, c.CreatedAt,
            CanEdit = await m.CanAsync(Permissions.MasterDataEdit, c.SiteId, c.DepartmentId, ct)
        };
    }
}

[ApiController, Authorize, Route("api/suppliers")]
public sealed class SuppliersController(AppDbContext db, SupplierService svc, SupplierQualityService quality, ModuleSupport m) : ControllerBase
{
    [HttpGet("scars")]
    [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> Scars([FromQuery] ScarStatus? status, [FromQuery] bool openOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.SupplierCorrectiveActions.AsNoTracking(), Permissions.SuppliersView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (openOnly) query = query.Where(x => x.Status != ScarStatus.Closed && x.Status != ScarStatus.Cancelled);
        return await Paging.PageAsync(query.OrderByDescending(x => x.CreatedAt).Select(x => new
        {
            x.Id, x.Number, x.Title, x.Status, x.IssuedOn, x.ResponseDueOn, x.ResponseReceivedOn, x.ResponseRejections,
            Supplier = db.Suppliers.Where(s => s.Id == x.SupplierId).Select(s => s.Code + " " + s.LegalName).First(),
            OwnerName = db.Users.Where(u => u.Id == x.OwnerId).Select(u => u.DisplayName).First()
        }), page, pageSize, ct);
    }

    [HttpPost("scars")]
    [RequirePermission(Permissions.SuppliersEdit)]
    public async Task<object> IssueScar(IssueScarRequest r, CancellationToken ct) { var c = await quality.IssueScarAsync(r, ct); return new { c.Id, c.Number }; }

    [HttpGet("scars/{id:guid}")]
    public async Task<object> Scar(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(SupplierQualityService.ScarType, id, ct);
        var c = await db.SupplierCorrectiveActions.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.SuppliersEdit, c.SiteId, c.DepartmentId, ct);
        var canVerify = c.OwnerId != m.UserId && await m.CanAsync(Permissions.SuppliersApprove, c.SiteId, c.DepartmentId, ct);
        var allowed = Allowed.Of(("respond", canEdit && c.Status == ScarStatus.Issued), ("review", canEdit && c.Status == ScarStatus.ResponseReceived), ("verify", canVerify && c.Status == ScarStatus.Verification));
        return new
        {
            c.Id, c.Number, c.SupplierId, c.NcrId, c.Title, c.Problem, c.IssuedOn, c.ResponseDueOn, c.Status, c.SupplierContainment, c.SupplierRootCause, c.CorrectiveActionPlan, c.SupplierRespondent,
            c.ResponseReceivedOn, c.ResponseRejections, c.ReviewNotes, c.EffectivenessNotes, c.WasEffective, c.ClosedAt, c.CreatedAt,
            Supplier = await db.Suppliers.Where(s => s.Id == c.SupplierId).Select(s => new { s.Id, s.Code, s.LegalName }).FirstAsync(ct),
            Ncr = c.NcrId == null ? null : await db.Ncrs.Where(n => n.Id == c.NcrId).Select(n => new { n.Id, n.Number, n.Title }).FirstAsync(ct),
            OwnerName = await db.Users.Where(u => u.Id == c.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            VerifiedByName = c.VerifiedById == null ? null : await db.Users.Where(u => u.Id == c.VerifiedById).Select(u => u.DisplayName).FirstAsync(ct),
            CanEdit = canEdit, Allowed = allowed
        };
    }

    [HttpPost("scars/{id:guid}/response")]
    public async Task<IActionResult> ScarResponse(Guid id, ScarResponseRequest r, CancellationToken ct) { await quality.RecordResponseAsync(id, r, ct); return NoContent(); }

    [HttpPost("scars/{id:guid}/review")]
    public async Task<IActionResult> ScarReview(Guid id, ScarReviewRequest r, CancellationToken ct) { await quality.ReviewResponseAsync(id, r, ct); return NoContent(); }

    [HttpPost("scars/{id:guid}/verify")]
    public async Task<IActionResult> ScarVerify(Guid id, ScarVerifyRequest r, CancellationToken ct) { await quality.VerifyAsync(id, r, ct); return NoContent(); }

    [HttpPost("{id:guid}/performance")]
    public async Task<object> RecordPerformance(Guid id, PerformanceRequest r, CancellationToken ct) => await quality.RecordPerformanceAsync(id, r, ct);

    [HttpPost("{id:guid}/reviews")]
    public async Task<object> RecordReview(Guid id, SupplierReviewRequest r, CancellationToken ct) { var x = await quality.RecordReviewAsync(id, r, ct); return new { x.Id }; }

    /// <summary>Suppliers whose periodic review is due within the given number of days (or overdue).</summary>
    [HttpGet("reviews-due")]
    [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> ReviewsDue([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, ct);
        var limit = (await m.TodayAsync(null, ct)).AddDays(Math.Clamp(days, 0, 365));
        return await query.Where(s => s.NextReviewOn != null && s.NextReviewOn <= limit && s.Status != SupplierStatus.Disqualified && s.Status != SupplierStatus.Prospective)
            .OrderBy(s => s.NextReviewOn).Select(s => new { s.Id, s.Code, s.LegalName, s.Status, s.NextReviewOn, s.Criticality }).ToListAsync(ct);
    }

    [HttpGet]
    [RequirePermission(Permissions.SuppliersView)]
    public async Task<object> List([FromQuery] string? q, [FromQuery] SupplierStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var query = await m.ScopeAsync(db.Suppliers.AsNoTracking(), Permissions.SuppliersView, ct);
        if (status is not null) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Code + " " + x.LegalName + " " + (x.OperatingName ?? "") + " " + x.Category, $"%{q.Trim()}%"));
        return await Paging.PageAsync(query.OrderBy(x => x.Code).Select(x => new
        {
            x.Id, x.Code, x.LegalName, x.OperatingName, x.Category, x.Criticality, x.Risk, x.Status, x.NextReviewOn, x.ApprovedOn,
            SiteCode = db.Sites.Where(s => s.Id == x.SiteId).Select(s => s.Code).First(),
            NextCertExpiry = db.SupplierCertifications.Where(c => c.SupplierId == x.Id).OrderBy(c => c.ExpiresOn).Select(c => (DateOnly?)c.ExpiresOn).FirstOrDefault()
        }), page, pageSize, ct);
    }

    [HttpPost]
    [RequirePermission(Permissions.SuppliersEdit)]
    public async Task<object> Create(SaveSupplierRequest r, CancellationToken ct) => new { (await svc.SaveAsync(null, r, ct)).Id };

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveSupplierRequest r, CancellationToken ct) { await svc.SaveAsync(id, r, ct); return NoContent(); }

    [HttpGet("{id:guid}")]
    public async Task<object> Get(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(SupplierService.RecordType, id, ct);
        var s = await db.Suppliers.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var canEdit = await m.CanAsync(Permissions.SuppliersEdit, s.SiteId, s.DepartmentId, ct);
        var canApprove = await m.CanAsync(Permissions.SuppliersApprove, s.SiteId, s.DepartmentId, ct);
        var st = s.Status;
        var allowed = Allowed.Of(("edit", canEdit), ("requestApproval", canEdit && st != SupplierStatus.PendingApproval),
            ("suspend", canApprove && st is SupplierStatus.Approved or SupplierStatus.Conditional),
            ("disqualify", canApprove && st != SupplierStatus.Disqualified && st != SupplierStatus.PendingApproval),
            ("scar", canEdit), ("performance", canEdit),
            ("review", canApprove && st is SupplierStatus.Approved or SupplierStatus.Conditional or SupplierStatus.Suspended or SupplierStatus.Disqualified));
        var ncrQ = await m.TryScopeAsync(db.Ncrs.AsNoTracking(), Permissions.NcrView, ct);
        var ncrScope = ncrQ is null ? null
            : await ncrQ.Where(n => n.SupplierId == id).OrderByDescending(n => n.CreatedAt).Take(50).Select(n => new { n.Id, n.Number, n.Title, n.Status, n.DetectedOn }).ToListAsync(ct);
        return new
        {
            s.Id, s.Code, s.LegalName, s.OperatingName, s.Category, s.SuppliedScope, s.Criticality, s.Risk, s.Status, s.ApprovedScope, s.StatusReason,
            s.ProposedStatus, s.ProposedScope, s.ApprovedOn, s.NextReviewOn, s.Address, s.Website, s.SiteId, s.DepartmentId, s.OwnerId, s.RowVersion, s.CreatedAt,
            OwnerName = await db.Users.Where(u => u.Id == s.OwnerId).Select(u => u.DisplayName).FirstAsync(ct),
            Contacts = await db.SupplierContacts.Where(c => c.SupplierId == id).OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.Role, c.Email, c.Phone }).ToListAsync(ct),
            Certifications = await db.SupplierCertifications.Where(c => c.SupplierId == id).OrderBy(c => c.ExpiresOn).Select(c => new { c.Id, c.Standard, c.CertificateNumber, c.Issuer, c.IssuedOn, c.ExpiresOn }).ToListAsync(ct),
            Overrides = await db.SupplierOverrides.Where(o => o.SupplierId == id).OrderByDescending(o => o.CreatedAt).Take(50)
                .Select(o => new { o.Id, o.RecordType, o.RecordId, o.Condition, o.Reason, o.CreatedAt, UserName = db.Users.Where(u => u.Id == o.UserId).Select(u => u.DisplayName).First() }).ToListAsync(ct),
            Check = await svc.CheckAsync(id, ct),
            Scars = await db.SupplierCorrectiveActions.Where(c => c.SupplierId == id).OrderByDescending(c => c.CreatedAt).Select(c => new { c.Id, c.Number, c.Title, c.Status, c.ResponseDueOn, c.ResponseReceivedOn }).ToListAsync(ct),
            Performance = await db.SupplierPerformance.Where(p => p.SupplierId == id).OrderByDescending(p => p.PeriodEnd).ToListAsync(ct),
            Reviews = await db.SupplierReviews.Where(r => r.SupplierId == id).OrderByDescending(r => r.ReviewedOn).ThenByDescending(r => r.CreatedAt)
                .Select(r => new { r.Id, r.ReviewedOn, r.Outcome, r.Summary, r.OverallScore, r.OpenScars, r.NcrsLast12Months, r.NextReviewOn, ReviewerName = db.Users.Where(u => u.Id == r.ReviewerId).Select(u => u.DisplayName).First() }).ToListAsync(ct),
            Ncrs = ncrScope,
            CanEdit = canEdit, CanApprove = canApprove, Allowed = allowed
        };
    }

    [HttpPost("{id:guid}/request-approval")]
    public async Task<IActionResult> RequestApproval(Guid id, SupplierApprovalRequest r, CancellationToken ct) { await svc.RequestApprovalAsync(id, r, ct); return NoContent(); }

    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid id, SupplierStatusRequest r, CancellationToken ct) { await svc.SuspendAsync(id, r.Reason, ct); return NoContent(); }

    [HttpPost("{id:guid}/disqualify")]
    public async Task<IActionResult> Disqualify(Guid id, SupplierStatusRequest r, CancellationToken ct) { await svc.DisqualifyAsync(id, r.Reason, ct); return NoContent(); }

    [HttpPost("{id:guid}/contacts")]
    public async Task<object> AddContact(Guid id, SaveContactRequest r, CancellationToken ct) => new { (await svc.AddContactAsync(id, r, ct)).Id };

    [HttpDelete("{id:guid}/contacts/{contactId:guid}")]
    public async Task<IActionResult> RemoveContact(Guid id, Guid contactId, CancellationToken ct) { await svc.RemoveContactAsync(id, contactId, ct); return NoContent(); }

    [HttpPost("{id:guid}/certifications")]
    public async Task<object> AddCertification(Guid id, SaveCertificationRequest r, CancellationToken ct) => new { (await svc.AddCertificationAsync(id, r, ct)).Id };

    [HttpGet("{id:guid}/check")]
    public async Task<SupplierCheck> Check(Guid id, CancellationToken ct) { await m.ViewAsync(SupplierService.RecordType, id, ct); return await svc.CheckAsync(id, ct); }
}
