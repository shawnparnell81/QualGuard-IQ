using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Imports;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.MasterLists;

/// <summary>A column of a governed master list, with its value type (text, date, number, yesno, status, user, site, department, record).</summary>
public sealed record MasterColumn(string Key, string Label, string Type);

/// <summary>
/// A governed master list type (TECH 12). Each list reads the module's own records, so the list and the module are always the same data;
/// nothing is kept in a second spreadsheet-like store. Edit, approval and revision control stay with the owning module.
/// </summary>
public sealed record MasterListType(string Key, string Name, string Module, string RecordType, string ViewPermission, string EditPermission,
    string? ImportTarget, bool RevisionControlled, bool Approval, string Description, IReadOnlyList<MasterColumn> Columns);

public sealed record MasterRow(Guid Id, string Url, bool Active, IReadOnlyDictionary<string, object?> Values);
public sealed record MasterListPage(MasterListType Type, int Total, int Page, int PageSize, IReadOnlyList<MasterRow> Rows, IReadOnlyDictionary<string, int> ByStatus, int Inactive);
public sealed record SaveMasterViewRequest(string Name, string Settings, bool IsDefault);

public sealed class MasterListService(AppDbContext db, ModuleSupport m)
{
    static MasterColumn C(string key, string label, string type = "text") => new(key, label, type);

    public static readonly IReadOnlyList<MasterListType> Types =
    [
        new("calibration-equipment", "Calibration master list", "Calibration", CalibrationService.EquipmentType, Permissions.CalibrationView, Permissions.CalibrationEdit, "master-equipment", false, false,
            "Equipment that needs calibration, with its range, accuracy, acceptance criteria, frequency, due dates, provider and status.",
            [C("number", "Equipment ID"), C("asset", "Asset ID"), C("name", "Equipment name"), C("category", "Type"), C("manufacturer", "Manufacturer"), C("model", "Model"), C("serial", "Serial number"),
             C("site", "Site", "site"), C("department", "Department", "department"), C("location", "Location"), C("owner", "Owner", "user"), C("range", "Range"), C("resolution", "Resolution"),
             C("accuracy", "Accuracy"), C("acceptance", "Acceptance criteria"), C("frequency", "Frequency (days)", "number"), C("last", "Last calibration", "date"), C("next", "Next due date", "date"),
             C("provider", "Provider"), C("status", "Status", "status"), C("certificate", "Certificate"), C("oot", "Out of tolerance", "yesno"), C("limited", "Limited use"), C("retired", "Retired", "yesno")]),
        new("controlled-documents", "Master document list", "Documents", "Document", Permissions.DocumentsView, Permissions.DocumentsEdit, "master-documents", true, true,
            "Every controlled document with its revision, status, dates, distribution, training requirement, retention rule and files.",
            [C("number", "Document number"), C("title", "Title"), C("category", "Category"), C("department", "Department", "department"), C("site", "Site", "site"), C("owner", "Owner", "user"),
             C("revision", "Revision"), C("status", "Status", "status"), C("effective", "Effective date", "date"), C("review", "Review date", "date"), C("expires", "Expiration date", "date"),
             C("distribution", "Distribution"), C("training", "Training requirement", "yesno"), C("retention", "Retention rule"), C("native", "Native file", "yesno"), C("pdf", "Controlled PDF", "yesno"),
             C("process", "Related process")]),
        new("drawings", "Engineering drawings", "Documents", "Drawing", Permissions.DrawingsView, Permissions.DrawingsEdit, "master-drawings", true, true, "Controlled engineering drawings with part number, size, sheets and revision.",
            [C("number", "Drawing number"), C("title", "Title"), C("part", "Part number"), C("size", "Size"), C("sheets", "Sheets", "number"), C("revision", "Revision"), C("status", "Status", "status"),
             C("effective", "Effective date", "date"), C("owner", "Owner", "user"), C("site", "Site", "site")]),
        new("specifications", "Specifications", "Documents", "Document", Permissions.DocumentsView, Permissions.DocumentsEdit, "master-documents", true, true, "Controlled documents of a specification type.",
            [C("number", "Number"), C("title", "Title"), C("category", "Type"), C("revision", "Revision"), C("status", "Status", "status"), C("effective", "Effective date", "date"), C("owner", "Owner", "user"), C("site", "Site", "site")]),
        new("forms", "Forms", "Documents", "FormTemplate", Permissions.FormsView, Permissions.FormsEdit, null, true, true, "Controlled form templates with their module, revision and status.",
            [C("number", "Template number"), C("title", "Title"), C("module", "Module"), C("revision", "Revision"), C("status", "Status", "status"), C("effective", "Effective date", "date"), C("owner", "Owner", "user"), C("site", "Site", "site")]),
        new("suppliers", "Approved supplier list", "Suppliers", "Supplier", Permissions.SuppliersView, Permissions.SuppliersEdit, "master-suppliers", false, true, "Suppliers with their approved scope, criticality, risk, status and review dates.",
            [C("code", "Code"), C("name", "Legal name"), C("operating", "Operating name"), C("category", "Category"), C("scope", "Approved scope"), C("criticality", "Criticality"), C("risk", "Risk"),
             C("status", "Status", "status"), C("approved", "Approved on", "date"), C("nextReview", "Next review", "date"), C("owner", "Owner", "user"), C("site", "Site", "site")]),
        new("customers", "Customers", "Master data", "Customer", Permissions.MasterDataView, Permissions.MasterDataEdit, "master-customers", false, false, "Customers and their contacts.",
            [C("code", "Code"), C("name", "Name"), C("contact", "Contact"), C("email", "E-mail"), C("phone", "Phone"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("employees", "Employees", "Production", "Employee", Permissions.ProductionView, Permissions.ProductionEdit, "master-employees", false, false, "Employees, their department and their user account.",
            [C("number", "Employee number"), C("name", "Name"), C("department", "Department"), C("user", "User", "user"), C("site", "Site", "site"), C("source", "Source"), C("status", "Status", "status")]),
        new("training-requirements", "Training requirements", "Training", "TrainingCourse", Permissions.TrainingView, Permissions.TrainingEdit, "master-training", false, false, "Training courses with the document they train on, validity and due period.",
            [C("number", "Course"), C("title", "Title"), C("document", "Document"), C("validity", "Valid for (months)", "number"), C("due", "Due within (days)", "number"), C("retrain", "Retrain on revision", "yesno"),
             C("department", "Department", "department"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("certifications", "Certifications", "Training", "EmployeeCertification", Permissions.TrainingView, Permissions.TrainingEdit, "master-skills", false, false, "Who holds which certified skill, until when.",
            [C("person", "Person", "user"), C("skill", "Skill"), C("certified", "Certified on", "date"), C("expires", "Expires on", "date"), C("reference", "Reference"), C("issuer", "Issued by"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("products", "Products", "Master data", "Product", Permissions.MasterDataView, Permissions.MasterDataEdit, "master-products", false, false, "Products with family, category and owner.",
            [C("number", "Product number"), C("name", "Name"), C("family", "Family"), C("category", "Category"), C("owner", "Owner", "user"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("parts", "Parts", "Master data", "Part", Permissions.MasterDataView, Permissions.MasterDataEdit, "master-parts", true, false, "Parts with revision, type, unit, product and material.",
            [C("number", "Part number"), C("name", "Name"), C("type", "Type"), C("revision", "Revision"), C("uom", "Unit"), C("product", "Product"), C("material", "Material"), C("special", "Special characteristics", "yesno"),
             C("site", "Site", "site"), C("status", "Status", "status")]),
        new("inspection-plans", "Inspection plans", "Inspection", "InspectionPlan", Permissions.InspectionView, Permissions.InspectionEdit, null, true, true, "Inspection plans with their type, product or part and owner.",
            [C("number", "Plan"), C("title", "Title"), C("type", "Inspection type"), C("product", "Product"), C("part", "Part"), C("owner", "Owner", "user"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("risks", "Risk register", "Risks", "Risk", Permissions.RisksView, Permissions.RisksEdit, "master-risks", false, true, "Risks with their scores before and after controls, owner and review date.",
            [C("number", "Risk"), C("kind", "Kind"), C("category", "Category"), C("title", "Title"), C("process", "Process"), C("initial", "Initial score", "number"), C("initialBand", "Initial band"),
             C("residualBand", "Residual band"), C("owner", "Owner", "user"), C("reviewDue", "Review due", "date"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("quality-objectives", "Quality objectives", "Objectives", "QualityObjective", Permissions.ObjectivesView, Permissions.ObjectivesEdit, null, false, true, "Quality objectives with metric, target, frequency and owner.",
            [C("number", "Objective"), C("title", "Title"), C("process", "Process"), C("metric", "Metric"), C("target", "Target", "number"), C("unit", "Unit"), C("frequency", "Frequency"), C("owner", "Owner", "user"),
             C("starts", "Starts", "date"), C("ends", "Ends", "date"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("audit-schedules", "Audit schedules", "Audits", "AuditProgram", Permissions.AuditsView, Permissions.AuditsEdit, null, false, true, "Planned audits from the audit programmes, by month.",
            [C("program", "Programme"), C("year", "Year", "number"), C("month", "Planned month", "number"), C("type", "Audit type"), C("subject", "Subject"), C("criteria", "Criteria"), C("lead", "Lead auditor", "user"),
             C("audit", "Audit"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("customer-requirements", "Customer requirements", "Engineering", "ProductRequirement", Permissions.EngineeringView, Permissions.EngineeringEdit, null, true, true, "Requirements that come from customers, with product, reference, revision and verification.",
            [C("number", "Requirement"), C("title", "Title"), C("customer", "Customer"), C("product", "Product"), C("reference", "Reference"), C("revision", "Revision", "number"), C("verification", "Verification"),
             C("owner", "Owner", "user"), C("site", "Site", "site"), C("status", "Status", "status")]),
        new("supplier-certifications", "Supplier certifications", "Suppliers", "Supplier", Permissions.SuppliersView, Permissions.SuppliersEdit, "master-supplier-certifications", false, false, "Certificates held by suppliers and when they expire.",
            [C("supplier", "Supplier"), C("standard", "Standard"), C("certificate", "Certificate number"), C("issuer", "Issuer"), C("issued", "Issued on", "date"), C("expires", "Expires on", "date"), C("status", "Status", "status")]),
    ];

    public static MasterListType Type(string key) => Types.FirstOrDefault(t => t.Key == key) ?? throw new NotFoundException("Master list");

    /// <summary>The list types the current user can see (the module's view permission at any site).</summary>
    public async Task<IReadOnlyList<object>> TypesAsync(CancellationToken ct)
    {
        var list = new List<object>();
        foreach (var t in Types)
            if (await m.CanAnyAsync(t.ViewPermission, ct)) list.Add(new { t.Key, t.Name, t.Module, t.Description, t.RevisionControlled, t.Approval, t.ImportTarget, columns = t.Columns.Count, canEdit = await m.CanAnyAsync(t.EditPermission, ct) });
        return list;
    }

    sealed record Lookups(Dictionary<Guid, string> Sites, Dictionary<Guid, string> Departments, Dictionary<Guid, string> Users);

    async Task<Lookups> LookupsAsync(CancellationToken ct) => new(
        await db.Sites.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, ct),
        await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct),
        await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct));

    const int Max = 50_000;

    /// <summary>Every row of a list the user may see, read from the module's records.</summary>
    async Task<List<MasterRow>> RowsAsync(MasterListType t, CancellationToken ct)
    {
        var L = await LookupsAsync(ct);
        string? S(Guid id) => L.Sites.GetValueOrDefault(id);
        string? D(Guid? id) => id is { } x ? L.Departments.GetValueOrDefault(x) : null;
        string? U(Guid? id) => id is { } x ? L.Users.GetValueOrDefault(x) : null;
        static Dictionary<string, object?> V(params (string, object?)[] kv) => kv.ToDictionary(x => x.Item1, x => x.Item2);
        async Task<IQueryable<T>?> Scope<T>(IQueryable<T> q) where T : class, Domain.Common.ISiteScoped => await m.TryScopeAsync(q, t.ViewPermission, ct);

        switch (t.Key)
        {
            case "calibration-equipment":
            {
                var q = await Scope(db.Equipment.AsNoTracking().Where(e => e.RequiresCalibration));
                if (q is null) return [];
                var rows = await q.OrderBy(e => e.Number).Take(Max).ToListAsync(ct);
                var ids = rows.Select(r => r.Id).ToList();
                var last = (await db.CalibrationEvents.AsNoTracking().Where(c => ids.Contains(c.EquipmentId)).Select(c => new { c.EquipmentId, c.PerformedOn, c.Result, c.CertificateNumber, c.CreatedAt }).ToListAsync(ct))
                    .GroupBy(c => c.EquipmentId).ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.PerformedOn).ThenByDescending(c => c.CreatedAt).First());
                return rows.Select(e =>
                {
                    var l = last.GetValueOrDefault(e.Id);
                    return new MasterRow(e.Id, $"/equipment/{e.Id}", e.Status != EquipmentStatus.Retired, V(("number", e.Number), ("asset", e.AssetNumber), ("name", e.Name), ("category", e.Category), ("manufacturer", e.Manufacturer),
                        ("model", e.Model), ("serial", e.SerialNumber), ("site", S(e.SiteId)), ("department", D(e.DepartmentId)), ("location", e.Location), ("owner", U(e.OwnerId)), ("range", e.MeasurementRange),
                        ("resolution", e.Resolution), ("accuracy", e.Accuracy ?? e.Tolerance), ("acceptance", e.AcceptanceCriteria), ("frequency", e.CalibrationIntervalDays), ("last", e.LastCalibratedOn), ("next", e.NextDueOn),
                        ("provider", e.CalibrationProvider), ("status", e.Status.ToString()), ("certificate", l?.CertificateNumber), ("oot", l is not null && l.Result is CalibrationResult.OutOfTolerance or CalibrationResult.Fail),
                        ("limited", e.LimitedUse ? (string.IsNullOrEmpty(e.LimitedUseNote) ? "Yes" : $"Yes: {e.LimitedUseNote}") : "No"), ("retired", e.Status == EquipmentStatus.Retired)));
                }).ToList();
            }
            case "controlled-documents" or "drawings" or "specifications" or "forms":
            {
                var kind = t.Key switch { "drawings" => DocumentKind.Drawing, "forms" => DocumentKind.FormTemplate, _ => DocumentKind.Document };
                var q = await Scope(db.Documents.AsNoTracking().Where(d => d.Kind == kind));
                if (q is null) return [];
                if (t.Key == "specifications") q = q.Where(d => EF.Functions.ILike(d.DocumentType, "%specification%"));
                var docs = await q.OrderBy(d => d.Number).Take(Max).ToListAsync(ct);
                var ids = docs.Select(d => d.Id).ToList();
                var revs = (await db.DocumentRevisions.AsNoTracking().Where(r => ids.Contains(r.DocumentId)).Select(r => new { r.DocumentId, r.Revision, r.Sequence, r.Status, r.EffectiveDate, r.TrainingRequired, r.NativeFileId, r.PreviewFileId }).ToListAsync(ct))
                    .GroupBy(r => r.DocumentId).ToDictionary(g => g.Key, g => g.FirstOrDefault(r => r.Status == RevisionStatus.Effective) ?? g.OrderByDescending(r => r.Sequence).First());
                var retention = await db.RetentionPolicies.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);
                return docs.Select(d =>
                {
                    var r = revs.GetValueOrDefault(d.Id);
                    var url = kind == DocumentKind.FormTemplate ? $"/forms/templates/{d.Id}" : kind == DocumentKind.Drawing ? $"/drawings/{d.Id}" : $"/documents/{d.Id}";
                    var status = d.Status == DocumentStatus.Active && r is not null ? r.Status.ToString() : d.Status.ToString();
                    return new MasterRow(d.Id, url, d.Status != DocumentStatus.Obsolete && !d.IsArchived, t.Key switch
                    {
                        "drawings" => V(("number", d.Number), ("title", d.Title), ("part", d.PartNumber), ("size", d.DrawingSize), ("sheets", d.SheetCount), ("revision", r?.Revision), ("status", status), ("effective", r?.EffectiveDate), ("owner", U(d.OwnerId)), ("site", S(d.SiteId))),
                        "forms" => V(("number", d.Number), ("title", d.Title), ("module", d.ModuleKey), ("revision", r?.Revision), ("status", status), ("effective", r?.EffectiveDate), ("owner", U(d.OwnerId)), ("site", S(d.SiteId))),
                        "specifications" => V(("number", d.Number), ("title", d.Title), ("category", d.DocumentType), ("revision", r?.Revision), ("status", status), ("effective", r?.EffectiveDate), ("owner", U(d.OwnerId)), ("site", S(d.SiteId))),
                        _ => V(("number", d.Number), ("title", d.Title), ("category", d.DocumentType), ("department", D(d.DepartmentId)), ("site", S(d.SiteId)), ("owner", U(d.OwnerId)), ("revision", r?.Revision), ("status", status),
                            ("effective", r?.EffectiveDate), ("review", d.NextReviewDue), ("expires", d.ExpiresOn), ("distribution", d.Distribution), ("training", r?.TrainingRequired ?? false),
                            ("retention", d.RetentionPolicyId is { } rp ? retention.GetValueOrDefault(rp) : null), ("native", r?.NativeFileId is not null), ("pdf", r?.PreviewFileId is not null), ("process", d.RelatedProcess)),
                    });
                }).ToList();
            }
            case "suppliers":
            {
                var q = await Scope(db.Suppliers.AsNoTracking());
                if (q is null) return [];
                return (await q.OrderBy(s => s.Code).Take(Max).ToListAsync(ct)).Select(s => new MasterRow(s.Id, $"/suppliers/{s.Id}", s.Status is not (SupplierStatus.Disqualified or SupplierStatus.Suspended),
                    V(("code", s.Code), ("name", s.LegalName), ("operating", s.OperatingName), ("category", s.Category), ("scope", s.ApprovedScope ?? s.SuppliedScope), ("criticality", s.Criticality.ToString()), ("risk", s.Risk.ToString()),
                      ("status", s.Status.ToString()), ("approved", s.ApprovedOn), ("nextReview", s.NextReviewOn), ("owner", U(s.OwnerId)), ("site", S(s.SiteId))))).ToList();
            }
            case "customers":
            {
                var q = await Scope(db.Customers.AsNoTracking());
                if (q is null) return [];
                return (await q.OrderBy(c => c.Code).Take(Max).ToListAsync(ct)).Select(c => new MasterRow(c.Id, $"/master-data/customers/{c.Id}", c.Status == MasterStatus.Active,
                    V(("code", c.Code), ("name", c.Name), ("contact", c.ContactName), ("email", c.Email), ("phone", c.Phone), ("site", S(c.SiteId)), ("status", c.Status.ToString())))).ToList();
            }
            case "employees":
            {
                var q = await Scope(db.Employees.AsNoTracking());
                if (q is null) return [];
                return (await q.OrderBy(e => e.Number).Take(Max).ToListAsync(ct)).Select(e => new MasterRow(e.Id, "/production/employees", e.Active,
                    V(("number", e.Number), ("name", e.Name), ("department", e.Department ?? D(e.DepartmentId)), ("user", U(e.UserId)), ("site", S(e.SiteId)), ("source", e.Source.ToString()), ("status", e.Active ? "Active" : "Inactive")))).ToList();
            }
            case "training-requirements":
            {
                var q = await Scope(db.TrainingCourses.AsNoTracking());
                if (q is null) return [];
                var rows = await q.OrderBy(c => c.Number).Take(Max).ToListAsync(ct);
                var docIds = rows.Where(r => r.DocumentId is not null).Select(r => r.DocumentId!.Value).ToList();
                var docNumbers = await db.Documents.AsNoTracking().Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => d.Number, ct);
                return rows.Select(c => new MasterRow(c.Id, $"/training/courses/{c.Id}", !c.IsArchived,
                    V(("number", c.Number), ("title", c.Title), ("document", c.DocumentId is { } di ? docNumbers.GetValueOrDefault(di) : null), ("validity", c.ValidityMonths), ("due", c.DueDays), ("retrain", c.RetrainOnRevision),
                      ("department", D(c.DepartmentId)), ("site", S(c.SiteId)), ("status", c.IsArchived ? "Archived" : "Active")))).ToList();
            }
            case "certifications":
            {
                var q = await Scope(db.EmployeeCertifications.AsNoTracking());
                if (q is null) return [];
                var rows = await q.OrderByDescending(c => c.CertifiedOn).Take(Max).ToListAsync(ct);
                var skills = await db.Skills.AsNoTracking().ToDictionaryAsync(s => s.Id, s => $"{s.Code} {s.Name}", ct);
                var today = await m.TodayAsync(null, ct);
                return rows.Select(c => new MasterRow(c.Id, "/training/skills", c.Status == CertificationStatus.Active && (c.ExpiresOn is null || c.ExpiresOn >= today),
                    V(("person", U(c.UserId)), ("skill", skills.GetValueOrDefault(c.SkillId)), ("certified", c.CertifiedOn), ("expires", c.ExpiresOn), ("reference", c.Reference), ("issuer", c.IssuedBy), ("site", S(c.SiteId)),
                      ("status", c.Status == CertificationStatus.Revoked ? "Revoked" : c.ExpiresOn < today ? "Expired" : "Active")))).ToList();
            }
            case "products":
            {
                var q = await Scope(db.Products.AsNoTracking());
                if (q is null) return [];
                var fam = await db.ProductFamilies.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name, ct);
                return (await q.OrderBy(p => p.Number).Take(Max).ToListAsync(ct)).Select(p => new MasterRow(p.Id, $"/master-data/products/{p.Id}", p.Status == MasterStatus.Active,
                    V(("number", p.Number), ("name", p.Name), ("family", p.FamilyId is { } f ? fam.GetValueOrDefault(f) : null), ("category", p.Category), ("owner", U(p.OwnerId)), ("site", S(p.SiteId)), ("status", p.Status.ToString())))).ToList();
            }
            case "parts":
            {
                var q = await Scope(db.Parts.AsNoTracking());
                if (q is null) return [];
                var prod = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Number, ct);
                return (await q.OrderBy(p => p.PartNumber).Take(Max).ToListAsync(ct)).Select(p => new MasterRow(p.Id, $"/master-data/parts/{p.Id}", p.Status == MasterStatus.Active,
                    V(("number", p.PartNumber), ("name", p.Name), ("type", p.PartType.ToString()), ("revision", p.Revision), ("uom", p.UnitOfMeasure), ("product", p.ProductId is { } pi ? prod.GetValueOrDefault(pi) : null),
                      ("material", p.Material), ("special", p.HasSpecialCharacteristics), ("site", S(p.SiteId)), ("status", p.Status.ToString())))).ToList();
            }
            case "inspection-plans":
            {
                var q = await Scope(db.InspectionPlans.AsNoTracking());
                if (q is null) return [];
                var prod = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Number, ct);
                var parts = await db.Parts.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.PartNumber, ct);
                return (await q.OrderBy(p => p.Number).Take(Max).ToListAsync(ct)).Select(p => new MasterRow(p.Id, $"/inspection/plans/{p.Id}", !p.IsObsolete,
                    V(("number", p.Number), ("title", p.Title), ("type", p.InspectionType), ("product", p.ProductId is { } a ? prod.GetValueOrDefault(a) : null), ("part", p.PartId is { } b ? parts.GetValueOrDefault(b) : null),
                      ("owner", U(p.OwnerId)), ("site", S(p.SiteId)), ("status", p.IsObsolete ? "Obsolete" : p.EffectiveRevisionId is null ? "Draft" : "Effective")))).ToList();
            }
            case "risks":
            {
                var q = await Scope(db.Risks.AsNoTracking());
                if (q is null) return [];
                return (await q.OrderBy(r => r.Number).Take(Max).ToListAsync(ct)).Select(r => new MasterRow(r.Id, $"/risks/{r.Id}", r.Status is not (RiskStatus.Closed or RiskStatus.Cancelled),
                    V(("number", r.Number), ("kind", r.Kind.ToString()), ("category", r.Category), ("title", r.Title), ("process", r.Process), ("initial", r.InitialScore), ("initialBand", r.InitialBand), ("residualBand", r.ResidualBand),
                      ("owner", U(r.OwnerId)), ("reviewDue", r.ReviewDueOn), ("site", S(r.SiteId)), ("status", r.Status.ToString())))).ToList();
            }
            case "quality-objectives":
            {
                var q = await Scope(db.QualityObjectives.AsNoTracking());
                if (q is null) return [];
                return (await q.OrderBy(o => o.Number).Take(Max).ToListAsync(ct)).Select(o => new MasterRow(o.Id, $"/objectives/{o.Id}", o.Status is not ObjectiveStatus.Retired,
                    V(("number", o.Number), ("title", o.Title), ("process", o.Process), ("metric", o.MetricName), ("target", o.Target), ("unit", o.Unit), ("frequency", o.Frequency.ToString()), ("owner", U(o.OwnerId)),
                      ("starts", o.StartsOn), ("ends", o.EndsOn), ("site", S(o.SiteId)), ("status", o.Status.ToString())))).ToList();
            }
            case "audit-schedules":
            {
                var q = await Scope(db.AuditPlanItems.AsNoTracking());
                if (q is null) return [];
                var items = await q.Take(Max).ToListAsync(ct);
                var progIds = items.Select(i => i.ProgramId).Distinct().ToList();
                var progs = await db.AuditPrograms.AsNoTracking().Where(p => progIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
                var auditIds = items.Where(i => i.AuditId is not null).Select(i => i.AuditId!.Value).ToList();
                var audits = await db.Audits.AsNoTracking().Where(a => auditIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Number, ct);
                return items.OrderBy(i => progs.GetValueOrDefault(i.ProgramId)?.Year).ThenBy(i => i.PlannedMonth).Select(i =>
                {
                    var p = progs.GetValueOrDefault(i.ProgramId);
                    return new MasterRow(i.Id, $"/audits/programs/{i.ProgramId}", p?.Status.ToString() is not ("Closed" or "Cancelled"),
                        V(("program", p is null ? null : $"{p.Number} {p.Title}"), ("year", p?.Year), ("month", i.PlannedMonth), ("type", i.AuditType), ("subject", i.Subject), ("criteria", i.Criteria), ("lead", U(i.LeadAuditorId)),
                          ("audit", i.AuditId is { } a ? audits.GetValueOrDefault(a) : null), ("site", S(i.SiteId)), ("status", i.AuditId is null ? "Planned" : "Scheduled")));
                }).ToList();
            }
            case "customer-requirements":
            {
                var q = await Scope(db.ProductRequirements.AsNoTracking().Where(r => r.Source == RequirementSource.Customer));
                if (q is null) return [];
                var cust = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
                var prod = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Number, ct);
                return (await q.OrderBy(r => r.Number).Take(Max).ToListAsync(ct)).Select(r => new MasterRow(r.Id, $"/engineering/requirements/{r.Id}", r.Status != RequirementStatus.Obsolete,
                    V(("number", r.Number), ("title", r.Title), ("customer", r.CustomerId is { } c ? cust.GetValueOrDefault(c) : null), ("product", r.ProductId is { } p ? prod.GetValueOrDefault(p) : null), ("reference", r.Reference),
                      ("revision", r.Revision), ("verification", r.Verification.ToString()), ("owner", U(r.OwnerId)), ("site", S(r.SiteId)), ("status", r.Status.ToString())))).ToList();
            }
            case "supplier-certifications":
            {
                var sq = await Scope(db.Suppliers.AsNoTracking());
                if (sq is null) return [];
                var today = await m.TodayAsync(null, ct);
                var rows = await (from c in db.SupplierCertifications.AsNoTracking() join s in sq on c.SupplierId equals s.Id orderby c.ExpiresOn select new { c, s.Code, s.LegalName, s.Id }).Take(Max).ToListAsync(ct);
                return rows.Select(x => new MasterRow(x.c.Id, $"/suppliers/{x.Id}", x.c.ExpiresOn >= today,
                    V(("supplier", $"{x.Code} {x.LegalName}"), ("standard", x.c.Standard), ("certificate", x.c.CertificateNumber), ("issuer", x.c.Issuer), ("issued", x.c.IssuedOn), ("expires", x.c.ExpiresOn),
                      ("status", x.c.ExpiresOn < today ? "Expired" : x.c.ExpiresOn < today.AddDays(60) ? "Expiring" : "Valid")))).ToList();
            }
        }
        return [];
    }

    static string Text(object? v) => v switch
    {
        null => "", bool b => b ? "Yes" : "No", DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal x => x.ToString(CultureInfo.InvariantCulture), int i => i.ToString(CultureInfo.InvariantCulture), _ => v.ToString() ?? "",
    };

    async Task<(MasterListType Type, List<MasterRow> Rows, int Inactive)> FilteredAsync(string type, string? q, bool includeInactive, string? status, string? sort, bool desc, CancellationToken ct)
    {
        var t = Type(type);
        if (!await m.CanAnyAsync(t.ViewPermission, ct)) throw new ForbiddenException();
        var all = await RowsAsync(t, ct);
        var inactive = all.Count(r => !r.Active);
        IEnumerable<MasterRow> rows = includeInactive ? all : all.Where(r => r.Active);
        if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(r => string.Equals(Text(r.Values.GetValueOrDefault("status")), status, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(q))
        {
            var words = q.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rows = rows.Where(r => words.All(w => r.Values.Values.Any(v => Text(v).Contains(w, StringComparison.OrdinalIgnoreCase))));
        }
        if (!string.IsNullOrWhiteSpace(sort) && t.Columns.Any(c => c.Key == sort))
        {
            var col = t.Columns.First(c => c.Key == sort);
            Func<MasterRow, IComparable> key = col.Type is "number" or "date"
                ? r => r.Values.GetValueOrDefault(sort) is IComparable c ? (IComparable)Convert.ToString(c is DateOnly d ? d.DayNumber.ToString("D8") : Convert.ToDecimal(c, CultureInfo.InvariantCulture).ToString("000000000000.0000", CultureInfo.InvariantCulture))! : ""
                : r => Text(r.Values.GetValueOrDefault(sort)).ToLowerInvariant();
            rows = desc ? rows.OrderByDescending(key) : rows.OrderBy(key);
        }
        return (t, rows.ToList(), inactive);
    }

    public async Task<MasterListPage> ListAsync(string type, string? q, bool includeInactive, string? status, string? sort, bool desc, int page, int pageSize, CancellationToken ct)
    {
        var (t, rows, inactive) = await FilteredAsync(type, q, includeInactive, status, sort, desc, ct);
        pageSize = Math.Clamp(pageSize, 1, 500); page = Math.Max(1, page);
        var byStatus = rows.GroupBy(r => Text(r.Values.GetValueOrDefault("status"))).ToDictionary(g => g.Key, g => g.Count());
        return new MasterListPage(t, rows.Count, page, pageSize, rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), byStatus, inactive);
    }

    /// <summary>The list as Excel or CSV (cells that look like formulas are neutralized in CSV), with the filters applied.</summary>
    public async Task<(byte[] Content, string ContentType, string Name)> ExportAsync(string type, string format, string? q, bool includeInactive, string? status, string? sort, bool desc, CancellationToken ct)
    {
        var (t, rows, _) = await FilteredAsync(type, q, includeInactive, status, sort, desc, ct);
        var stamp = (await m.TodayAsync(null, ct)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (format == "csv")
        {
            var sb = new StringBuilder();
            sb.AppendJoin(",", t.Columns.Select(c => CsvParser.SafeCell(c.Label))).Append("\r\n");
            foreach (var r in rows) sb.AppendJoin(",", t.Columns.Select(c => CsvParser.SafeCell(Text(r.Values.GetValueOrDefault(c.Key))))).Append("\r\n");
            return (new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"{t.Name} {stamp}.csv");
        }
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(t.Name.Length > 31 ? t.Name[..31] : t.Name);
        for (var i = 0; i < t.Columns.Count; i++) { ws.Cell(1, i + 1).Value = t.Columns[i].Label; ws.Cell(1, i + 1).Style.Font.Bold = true; }
        for (var r = 0; r < rows.Count; r++)
            for (var i = 0; i < t.Columns.Count; i++)
            {
                var v = rows[r].Values.GetValueOrDefault(t.Columns[i].Key);
                var cell = ws.Cell(r + 2, i + 1);
                switch (v)
                {
                    case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
                    case int n: cell.Value = n; break;
                    case decimal x: cell.Value = x; break;
                    default: cell.Value = Text(v); break; // text, never a formula
                }
            }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
        var ms = new MemoryStream();
        wb.SaveAs(ms);
        return (ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{t.Name} {stamp}.xlsx");
    }

    /// <summary>Change history of the list: the audit trail of the records it shows, newest first.</summary>
    public async Task<IReadOnlyList<object>> HistoryAsync(string type, int take, CancellationToken ct)
    {
        var (t, rows, _) = await FilteredAsync(type, null, true, null, null, false, ct);
        var ids = rows.Select(r => r.Id).ToList();
        var entityType = t.Key switch
        {
            "calibration-equipment" => nameof(Equipment), "controlled-documents" or "drawings" or "specifications" or "forms" => nameof(ControlledDocument), "suppliers" => nameof(Supplier), "customers" => nameof(Customer),
            "employees" => nameof(Employee), "training-requirements" => nameof(TrainingCourse), "certifications" => nameof(EmployeeCertification), "products" => nameof(Product), "parts" => nameof(Part),
            "inspection-plans" => nameof(InspectionPlan), "risks" => nameof(RiskItem), "quality-objectives" => nameof(QualityObjective), "audit-schedules" => nameof(AuditPlanItem),
            "customer-requirements" => nameof(ProductRequirement), _ => nameof(SupplierCertification),
        };
        return await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == entityType && ids.Contains(a.EntityId)).OrderByDescending(a => a.OccurredAt).Take(Math.Clamp(take, 1, 500))
            .Select(a => (object)new { a.OccurredAt, a.UserName, a.Action, a.Description, a.EntityId }).ToListAsync(ct);
    }

    // saved views, per person and list
    static string ListKey(string type) => $"master-list:{Type(type).Key}";

    public async Task<IReadOnlyList<object>> ViewsAsync(string type, CancellationToken ct) =>
        await db.SavedListViews.AsNoTracking().Where(v => v.UserId == m.UserId && v.ListKey == ListKey(type)).OrderBy(v => v.Name).Select(v => (object)new { v.Id, v.Name, v.Settings, v.IsDefault }).ToListAsync(ct);

    public async Task<Guid> SaveViewAsync(string type, SaveMasterViewRequest req, CancellationToken ct)
    {
        ModuleSupport.Required(req.Name, "Name", 100);
        if (req.Settings is { Length: > 4000 }) throw new AppException("The view settings are too long.");
        try { JsonDocument.Parse(req.Settings ?? "{}"); } catch (JsonException) { throw new AppException("The view settings are not valid."); }
        var key = ListKey(type); var me = m.UserId; var name = req.Name.Trim();
        if (req.IsDefault) await db.SavedListViews.Where(v => v.UserId == me && v.ListKey == key && v.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(v => v.IsDefault, false), ct);
        var v = await db.SavedListViews.FirstOrDefaultAsync(x => x.UserId == me && x.ListKey == key && x.Name == name, ct);
        if (v is null) { v = new SavedListView { UserId = me, ListKey = key, Name = name }; db.SavedListViews.Add(v); }
        v.Settings = req.Settings ?? "{}"; v.IsDefault = req.IsDefault;
        await db.SaveChangesAsync(ct);
        return v.Id;
    }

    public async Task DeleteViewAsync(string type, Guid id, CancellationToken ct)
    {
        var key = ListKey(type);
        var v = await db.SavedListViews.FirstOrDefaultAsync(x => x.Id == id && x.UserId == m.UserId && x.ListKey == key, ct) ?? throw new NotFoundException("View");
        db.SavedListViews.Remove(v);
        await db.SaveChangesAsync(ct);
    }
}
