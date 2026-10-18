using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using static QualGuardIQ.Infrastructure.Modules.QualityOps.QualityEventService;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record RegisterField(string Key, string Label, string Type, bool Required = false, IReadOnlyList<string>? Options = null);
/// <summary>A governed register: its fields, its statuses and which status changes are allowed.</summary>
public sealed record RegisterDefinition(RegisterKind Kind, string Key, string Name, string Description, IReadOnlyList<RegisterField> Fields, IReadOnlyList<string> Statuses,
    IReadOnlyDictionary<string, string[]> Transitions, IReadOnlyList<string> ClosedStatuses, bool UsesSupplier, bool UsesCustomer, bool UsesPart, bool NeedsSetting = false);
public sealed record RegisterInput(Guid SiteId, Guid? DepartmentId, string Title, string? Summary, Guid? SupplierId, Guid? CustomerId, Guid? PartId, Guid? OwnerId,
    DateOnly? EffectiveOn, DateOnly? ReviewDueOn, DateOnly? ExpiresOn, Dictionary<string, JsonElement>? Details, uint RowVersion = 0);

/// <summary>
/// Governed registers on one shared record: regulatory intelligence, quality agreements, approved laboratories, contract manufacturers,
/// stability programs, laboratory samples (when switched on) and supplier change notifications. Each register defines its fields
/// and status flow here; values are validated against the definition, and every change is audited.
/// </summary>
public sealed class RegisterService(AppDbContext db, ModuleSupport m, INumberingService numbers, QualityEventService events, INotifier notifier)
{
    public const string RecordType = "RegisterEntry";

    static RegisterField F(string key, string label, string type = "text", bool required = false, params string[] options) => new(key, label, type, required, options.Length == 0 ? null : options);
    static Dictionary<string, string[]> Tr(params (string From, string[] To)[] t) => t.ToDictionary(x => x.From, x => x.To);

    public static readonly IReadOnlyList<RegisterDefinition> Definitions =
    [
        new(RegisterKind.RegulatoryIntelligence, "regulatory-intelligence", "Regulatory intelligence", "Changes in regulations, standards and customer requirements, their assessment and the actions they trigger.",
            [F("source", "Source or authority", required: true), F("reference", "Reference or citation", required: true), F("jurisdiction", "Jurisdiction or market"), F("publishedOn", "Published on", "date"),
             F("applicableFrom", "Applicable from", "date"), F("impact", "Impact", "choice", true, "None", "Low", "Medium", "High"), F("affectedProcesses", "Affected processes", "textarea"),
             F("assessment", "Assessment", "textarea"), F("actions", "Actions required", "textarea")],
            ["Identified", "Under assessment", "Action required", "Implemented", "Not applicable"],
            Tr(("Identified", ["Under assessment", "Not applicable"]), ("Under assessment", ["Action required", "Not applicable"]), ("Action required", ["Implemented"])),
            ["Implemented", "Not applicable"], false, false, false),
        new(RegisterKind.QualityAgreement, "quality-agreements", "Quality agreements", "Quality agreements with suppliers, contract manufacturers and customers: scope, responsibilities, revision and renewal.",
            [F("partyType", "Party type", "choice", true, "Supplier", "Customer", "Contract manufacturer", "Laboratory"), F("agreementNumber", "Agreement number", required: true), F("revision", "Revision", required: true),
             F("scope", "Scope", "textarea", true), F("responsibilities", "Responsibility matrix", "textarea"), F("changeNotificationDays", "Change notification period (days)", "number"),
             F("signedOn", "Signed on", "date"), F("documentReference", "Controlled document reference")],
            ["Draft", "In negotiation", "Active", "Under revision", "Expired", "Terminated"],
            Tr(("Draft", ["In negotiation", "Terminated"]), ("In negotiation", ["Active", "Terminated"]), ("Active", ["Under revision", "Expired", "Terminated"]), ("Under revision", ["Active", "Terminated"]), ("Expired", ["Under revision", "Terminated"])),
            ["Terminated"], true, true, false),
        new(RegisterKind.ApprovedLaboratory, "approved-laboratories", "Approved laboratories", "Internal and external laboratories approved for testing and calibration, with accreditation scope and expiry.",
            [F("laboratoryType", "Laboratory type", "choice", true, "Internal", "External", "Customer-designated"), F("accreditationBody", "Accreditation body"), F("accreditationNumber", "Accreditation number"),
             F("scope", "Approved scope of tests", "textarea", true), F("methods", "Test methods"), F("lastAssessedOn", "Last assessed on", "date"), F("contact", "Contact")],
            ["Pending approval", "Approved", "Conditional", "Suspended", "Removed"],
            Tr(("Pending approval", ["Approved", "Conditional", "Removed"]), ("Approved", ["Conditional", "Suspended", "Removed"]), ("Conditional", ["Approved", "Suspended", "Removed"]), ("Suspended", ["Approved", "Removed"])),
            ["Removed"], true, false, false),
        new(RegisterKind.ContractManufacturer, "contract-manufacturers", "Contract manufacturers", "Contract manufacturers: products made, agreements, oversight activities and shared actions.",
            [F("products", "Products or processes", "textarea", true), F("qualityAgreement", "Quality agreement number"), F("oversight", "Oversight plan", "textarea"),
             F("lastAuditOn", "Last audit on", "date"), F("nextAuditOn", "Next audit on", "date"), F("sharedActions", "Shared action plan", "textarea"), F("portalAccess", "Portal access", "checkbox")],
            ["Onboarding", "Qualified", "Under review", "Disqualified"],
            Tr(("Onboarding", ["Qualified", "Disqualified"]), ("Qualified", ["Under review", "Disqualified"]), ("Under review", ["Qualified", "Disqualified"])),
            ["Disqualified"], true, false, false),
        new(RegisterKind.StabilityProgram, "stability-programs", "Stability programs", "Stability study configuration: storage conditions, pull points, tests and acceptance criteria.",
            [F("protocol", "Protocol reference", required: true), F("storageConditions", "Storage conditions", "textarea", true), F("pullPoints", "Pull points (months, comma separated)", required: true),
             F("tests", "Tests at each pull point", "textarea", true), F("acceptanceCriteria", "Acceptance criteria", "textarea", true), F("batches", "Batches on study"), F("startedOn", "Study start", "date")],
            ["Planned", "Active", "On hold", "Completed", "Cancelled"],
            Tr(("Planned", ["Active", "Cancelled"]), ("Active", ["On hold", "Completed"]), ("On hold", ["Active", "Cancelled"])),
            ["Completed", "Cancelled"], false, false, true),
        new(RegisterKind.LaboratorySample, "laboratory-samples", "Laboratory samples", "Sample tracking from receipt to disposal, where the organization switches it on.",
            [F("sampleId", "Sample identifier", required: true), F("sampleType", "Sample type", "choice", true, "Raw material", "In-process", "Finished product", "Stability", "Retain", "Environmental"),
             F("lotNumber", "Lot or batch"), F("receivedAt", "Received at", "date"), F("storageLocation", "Storage location"), F("tests", "Tests requested", "textarea"),
             F("custody", "Chain of custody notes", "textarea"), F("disposalOn", "Disposal on", "date")],
            ["Received", "In testing", "Tested", "Retained", "Disposed"],
            Tr(("Received", ["In testing", "Retained", "Disposed"]), ("In testing", ["Tested"]), ("Tested", ["Retained", "Disposed"]), ("Retained", ["Disposed"])),
            ["Disposed"], false, false, true, NeedsSetting: true),
        new(RegisterKind.SupplierChangeNotification, "supplier-change-notifications", "Supplier change notifications", "Changes notified by suppliers (process, material, site, sub-tier), their evaluation and decision.",
            [F("changeType", "Change type", "choice", true, "Process", "Material", "Manufacturing site", "Sub-tier supplier", "Tooling", "Design", "Other"), F("supplierReference", "Supplier reference"),
             F("description", "Description of the change", "textarea", true), F("plannedOn", "Planned implementation", "date"), F("affectedParts", "Affected parts"),
             F("evaluation", "Evaluation", "textarea"), F("requalification", "Requalification needed", "checkbox"), F("decisionNotes", "Decision notes", "textarea")],
            ["Received", "Under evaluation", "Approved", "Approved with conditions", "Rejected", "Implemented"],
            Tr(("Received", ["Under evaluation"]), ("Under evaluation", ["Approved", "Approved with conditions", "Rejected"]), ("Approved", ["Implemented"]), ("Approved with conditions", ["Implemented"])),
            ["Rejected", "Implemented"], true, false, true),
    ];

    public static RegisterDefinition Definition(string key) => Definitions.FirstOrDefault(d => d.Key == key) ?? throw new NotFoundException("Register");
    public static RegisterDefinition Definition(RegisterKind kind) => Definitions.First(d => d.Kind == kind);

    public async Task<RegisterEntry> CreateAsync(string key, RegisterInput r, CancellationToken ct)
    {
        var def = Definition(key);
        await m.RequireAsync(Permissions.QualityOpsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        if (def.NeedsSetting && !(await events.GetSettingsAsync(ct)).LabSampleTracking) throw new AppException("Laboratory sample tracking is not switched on for this organization.");
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var x = new RegisterEntry { Kind = def.Kind, Status = def.Statuses[0], OwnerId = owner, SiteId = r.SiteId, DepartmentId = r.DepartmentId, Number = await numbers.NextAsync(RecordType, r.SiteId, ct) };
        await ApplyAsync(def, x, r, ct);
        db.RegisterEntries.Add(x);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, x.Summary ?? x.Title, ct);
        await tx.CommitAsync(ct);
        return x;
    }

    public async Task UpdateAsync(Guid id, RegisterInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        var def = Definition(x.Kind);
        if (x.Archived || def.ClosedStatuses.Contains(x.Status)) throw new ConflictException("This entry is closed and cannot be edited.");
        if (r.SiteId != x.SiteId) throw new AppException("The site of an entry cannot change.");
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        await ApplyAsync(def, x, r, ct);
        if (r.OwnerId is { } o && o != x.OwnerId) { await m.RequireActiveUserAsync(o, "owner", ct); x.OwnerId = o; }
        x.DepartmentId = r.DepartmentId;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, x.Summary ?? x.Title, ct);
    }

    private async Task ApplyAsync(RegisterDefinition def, RegisterEntry x, RegisterInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.Summary is { Length: > 4000 }) throw new AppException("The summary is limited to 4000 characters.");
        if (r.SupplierId is { } s) { if (!def.UsesSupplier) throw new AppException("This register does not take a supplier."); if (!await db.Suppliers.AnyAsync(e => e.Id == s, ct)) throw new AppException("The supplier does not exist."); }
        if (r.CustomerId is { } c) { if (!def.UsesCustomer) throw new AppException("This register does not take a customer."); if (!await db.Customers.AnyAsync(e => e.Id == c, ct)) throw new AppException("The customer does not exist."); }
        if (r.PartId is { } p) { if (!def.UsesPart) throw new AppException("This register does not take a part."); if (!await db.Parts.AnyAsync(e => e.Id == p, ct)) throw new AppException("The part does not exist."); }
        if (def.Kind == RegisterKind.SupplierChangeNotification && r.SupplierId is null) throw new AppException("Choose the supplier that notified the change.");
        if (r.ExpiresOn is { } ex && r.EffectiveOn is { } ef && ex < ef) throw new AppException("The entry cannot expire before it takes effect.");
        x.Title = r.Title.Trim(); x.Summary = T(r.Summary); x.SupplierId = r.SupplierId; x.CustomerId = r.CustomerId; x.PartId = r.PartId;
        x.EffectiveOn = r.EffectiveOn; x.ReviewDueOn = r.ReviewDueOn; x.ExpiresOn = r.ExpiresOn;
        x.Details = ValidateDetails(def, r.Details ?? []).ToJsonString();
    }

    /// <summary>Checks every value against the register's fields; unknown keys are refused.</summary>
    public static JsonObject ValidateDetails(RegisterDefinition def, Dictionary<string, JsonElement> values)
    {
        var o = new JsonObject();
        foreach (var k in values.Keys) if (def.Fields.All(f => f.Key != k)) throw new AppException($"Unknown field '{k}' for {def.Name}.");
        foreach (var f in def.Fields)
        {
            values.TryGetValue(f.Key, out var v);
            var empty = v.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null || (v.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(v.GetString()));
            if (empty)
            {
                if (f.Required) throw new AppException($"{f.Label} is required.");
                continue;
            }
            switch (f.Type)
            {
                case "number":
                    if (!(v.ValueKind == JsonValueKind.Number || (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, out _))))
                        throw new AppException($"{f.Label} must be a number.");
                    o[f.Key] = v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : decimal.Parse(v.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "checkbox":
                    if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new AppException($"{f.Label} must be yes or no.");
                    o[f.Key] = v.GetBoolean();
                    break;
                case "date":
                    if (v.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(v.GetString(), "yyyy-MM-dd", out _)) throw new AppException($"{f.Label} must be a date (yyyy-mm-dd).");
                    o[f.Key] = v.GetString();
                    break;
                case "choice":
                    if (v.ValueKind != JsonValueKind.String || !f.Options!.Contains(v.GetString()!)) throw new AppException($"Choose a listed value for {f.Label}.");
                    o[f.Key] = v.GetString();
                    break;
                default:
                    if (v.ValueKind != JsonValueKind.String) throw new AppException($"{f.Label} must be text.");
                    var s = v.GetString()!.Trim();
                    if (s.Length > (f.Type == "textarea" ? 10000 : 500)) throw new AppException($"{f.Label} is too long.");
                    o[f.Key] = s;
                    break;
            }
        }
        return o;
    }

    /// <summary>Moves the entry along its register's status flow.</summary>
    public async Task TransitionAsync(Guid id, string status, string? comment, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        var def = Definition(x.Kind);
        if (x.Archived) throw new ConflictException("This entry is archived.");
        if (!def.Transitions.TryGetValue(x.Status, out var allowed) || !allowed.Contains(status)) throw new ConflictException($"{def.Name}: an entry cannot move from '{x.Status}' to '{status}'.");
        if (def.Kind == RegisterKind.SupplierChangeNotification && status.StartsWith("Approved", StringComparison.Ordinal))
        {
            await m.RequireAsync(Permissions.QualityOpsApprove, x.SiteId, x.DepartmentId, ct);
            var d = JsonNode.Parse(x.Details)!.AsObject();
            if (d["evaluation"] is null) throw new AppException("Record the evaluation before approving the change.");
        }
        if (comment is { Length: > 4000 }) throw new AppException("The comment is limited to 4000 characters.");
        x.Status = status;
        await db.SaveChangesAsync(ct);
        if (x.OwnerId != m.UserId) await notifier.NotifyAsync([x.OwnerId], $"{x.Number} is now {status}", comment, await m.RecordAsync(RecordType, x.Id, ct), email: false, ct: ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        var def = Definition(x.Kind);
        if (!def.ClosedStatuses.Contains(x.Status)) throw new ConflictException("Only a closed entry can be archived.");
        x.Archived = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task<RegisterEntry> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.RegisterEntries.FirstAsync(x => x.Id == id, ct);
    }
}
