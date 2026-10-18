using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Complaints;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.QualityOps;

public sealed record ScaleLevel(int Value, string Label);
public sealed record SeverityLevel(string Name, string Color, int ResponseHours, bool Escalate, int Order);
public sealed record SeverityCell(int Likelihood, int Impact, string Severity);
public sealed record QualityOpsSettingsDto(List<ScaleLevel> Likelihood, List<ScaleLevel> Impact, List<SeverityLevel> Severities, List<SeverityCell> Cells,
    List<string> Categories, List<string> Classifications, int RecurrenceWindowDays, int SystemicThreshold, bool LabSampleTracking, uint RowVersion = 0);

public sealed record QualityEventInput(Guid SiteId, Guid? DepartmentId, string Title, string Description, string? Category, DateTimeOffset? OccurredAt, string? Location, string? Process,
    Guid? PartId, Guid? ProductId, Guid? SupplierId, Guid? CustomerId, string? LotNumber, QualityEventSource Source = QualityEventSource.Manual, string? ClientReference = null, string? ExternalSystem = null);
public sealed record TriageInput(string? Category, string Classification, int Likelihood, int Impact, string? Notes);
public sealed record EscalateInput(string Target, string? RecordType, Guid? RecordId, string? Title, Guid? OwnerId);
public sealed record IntakeResult(Guid Id, string Number, bool Duplicate);

/// <summary>
/// Quality event intake, triage, classification against the configured severity matrix, escalation and recurrence detection.
/// Intake channels: the web, mobile devices (offline submissions carry a client reference and are stored once), the supplier and
/// customer portals and integrations (environmental monitoring, safety and EHS, complaint automation) through API clients.
/// </summary>
public sealed class QualityEventService(AppDbContext db, ModuleSupport m, INumberingService numbers, INotifier notifier, IRecordRegistry registry)
{
    public const string RecordType = "QualityEvent";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly QualityOpsSettingsDto Defaults = new(
        [new(1, "Rare"), new(2, "Unlikely"), new(3, "Possible"), new(4, "Likely"), new(5, "Almost certain")],
        [new(1, "Negligible"), new(2, "Minor"), new(3, "Moderate"), new(4, "Major"), new(5, "Severe")],
        [new("Low", "#2e7d32", 120, false, 1), new("Medium", "#f9a825", 48, false, 2), new("High", "#ef6c00", 24, true, 3), new("Critical", "#c62828", 4, true, 4)],
        DefaultCells(),
        ["Product", "Process", "Supplier", "Customer", "Equipment", "Environment", "Safety", "Documentation", "Laboratory", "Other"],
        ["Nonconformance", "Deviation", "Complaint", "Near miss", "Observation", "Laboratory result", "Environmental excursion", "Safety incident"],
        90, 3, false);

    static List<SeverityCell> DefaultCells()
    {
        var cells = new List<SeverityCell>();
        for (var l = 1; l <= 5; l++)
            for (var i = 1; i <= 5; i++)
            {
                var score = l * i;
                cells.Add(new(l, i, score >= 16 ? "Critical" : score >= 10 ? "High" : score >= 5 ? "Medium" : "Low"));
            }
        return cells;
    }

    public async Task<QualityOpsSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var s = await db.QualityOpsSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return s is null ? Defaults : new(L<ScaleLevel>(s.LikelihoodLevels), L<ScaleLevel>(s.ImpactLevels), L<SeverityLevel>(s.SeverityLevels), L<SeverityCell>(s.Cells),
            L<string>(s.Categories), L<string>(s.Classifications), s.RecurrenceWindowDays, s.SystemicThreshold, s.LabSampleTracking, s.RowVersion);
    }

    public async Task SaveSettingsAsync(QualityOpsSettingsDto r, CancellationToken ct)
    {
        Validate(r);
        var s = await db.QualityOpsSettings.FirstOrDefaultAsync(ct);
        if (s is null) db.QualityOpsSettings.Add(s = new QualityOpsSettings());
        else db.Entry(s).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        s.LikelihoodLevels = J(r.Likelihood.OrderBy(x => x.Value)); s.ImpactLevels = J(r.Impact.OrderBy(x => x.Value)); s.SeverityLevels = J(r.Severities.OrderBy(x => x.Order));
        s.Cells = J(r.Cells.OrderBy(c => c.Likelihood).ThenBy(c => c.Impact)); s.Categories = J(r.Categories.Select(x => x.Trim()).Distinct());
        s.Classifications = J(r.Classifications.Select(x => x.Trim()).Distinct()); s.RecurrenceWindowDays = r.RecurrenceWindowDays; s.SystemicThreshold = r.SystemicThreshold;
        s.LabSampleTracking = r.LabSampleTracking;
        await db.SaveChangesAsync(ct);
    }

    public static void Validate(QualityOpsSettingsDto r)
    {
        void Scale(List<ScaleLevel> l, string name)
        {
            if (l.Count is < 2 or > 10) throw new AppException($"The {name} scale needs 2 to 10 levels.");
            if (l.Any(x => x.Value < 1) || l.Select(x => x.Value).Distinct().Count() != l.Count) throw new AppException($"{name} values must be unique positive numbers.");
            if (l.Any(x => string.IsNullOrWhiteSpace(x.Label))) throw new AppException($"Every {name} level needs a label.");
        }
        Scale(r.Likelihood, "likelihood"); Scale(r.Impact, "impact");
        if (r.Severities.Count == 0 || r.Severities.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.ResponseHours < 1 || !System.Text.RegularExpressions.Regex.IsMatch(x.Color ?? "", "^#[0-9a-fA-F]{6}$")))
            throw new AppException("Each severity needs a name, a #RRGGBB color and a response time of at least one hour.");
        if (r.Severities.Select(x => x.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Severities.Count) throw new AppException("Severity names must be unique.");
        var names = r.Severities.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var l in r.Likelihood)
            foreach (var i in r.Impact)
            {
                var cell = r.Cells.Where(c => c.Likelihood == l.Value && c.Impact == i.Value).ToList();
                if (cell.Count != 1) throw new AppException($"The matrix needs exactly one severity for likelihood {l.Label} and impact {i.Label}.");
                if (!names.Contains(cell[0].Severity)) throw new AppException($"The matrix cell for likelihood {l.Label} and impact {i.Label} uses an unknown severity.");
            }
        if (r.Cells.Count != r.Likelihood.Count * r.Impact.Count) throw new AppException("The matrix has cells outside the likelihood and impact scales.");
        if (r.Categories.Count == 0 || r.Categories.Any(string.IsNullOrWhiteSpace)) throw new AppException("Define at least one event category.");
        if (r.Classifications.Count == 0 || r.Classifications.Any(string.IsNullOrWhiteSpace)) throw new AppException("Define at least one event classification.");
        if (r.RecurrenceWindowDays is < 1 or > 1825) throw new AppException("The recurrence window must be between 1 and 1825 days.");
        if (r.SystemicThreshold is < 2 or > 100) throw new AppException("The systemic threshold must be between 2 and 100.");
    }

    /// <summary>Severity for a likelihood and impact from the configured matrix.</summary>
    public static SeverityLevel Rate(QualityOpsSettingsDto s, int likelihood, int impact)
    {
        if (!s.Likelihood.Any(x => x.Value == likelihood)) throw new AppException("Choose a likelihood from the configured scale.");
        if (!s.Impact.Any(x => x.Value == impact)) throw new AppException("Choose an impact from the configured scale.");
        var name = s.Cells.First(c => c.Likelihood == likelihood && c.Impact == impact).Severity;
        return s.Severities.First(x => x.Name == name);
    }

    /// <summary>Reports an event. A repeated submission with the same source and client reference returns the stored event.</summary>
    public async Task<IntakeResult> ReportAsync(QualityEventInput r, CancellationToken ct)
    {
        if (!await m.CanAsync(Permissions.QualityEventsReport, r.SiteId, r.DepartmentId, ct)) await m.RequireAsync(Permissions.QualityOpsEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var reference = T(r.ClientReference);
        if (reference is { Length: > 120 }) throw new AppException("The client reference is limited to 120 characters.");
        if (reference is not null)
        {
            var existing = await db.QualityEvents.AsNoTracking().Where(e => e.Source == r.Source && e.ClientReference == reference).Select(e => new { e.Id, e.Number }).FirstOrDefaultAsync(ct);
            if (existing is not null) return new(existing.Id, existing.Number, true);
        }
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Description, "Description", 10000);
        var s = await GetSettingsAsync(ct);
        var category = T(r.Category);
        if (category is not null && !s.Categories.Contains(category)) throw new AppException("Choose a configured event category.");
        await RefsAsync(r.PartId, r.ProductId, r.SupplierId, r.CustomerId, ct);
        if (r.OccurredAt is { } at && at > m.Now.AddMinutes(10)) throw new AppException("The event cannot have happened in the future.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var x = new QualityEvent
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Source = r.Source, ClientReference = reference, Title = r.Title.Trim(), Description = r.Description.Trim(),
            Category = category, OccurredAt = r.OccurredAt ?? m.Now, Location = T(r.Location), Process = T(r.Process), PartId = r.PartId, ProductId = r.ProductId,
            SupplierId = r.SupplierId, CustomerId = r.CustomerId, LotNumber = T(r.LotNumber), ExternalSystem = T(r.ExternalSystem), ReportedById = m.UserId,
            Status = QualityEventStatus.New, SiteId = r.SiteId, DepartmentId = r.DepartmentId
        };
        db.QualityEvents.Add(x);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (reference is not null)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var dup = await db.QualityEvents.AsNoTracking().Where(e => e.Source == r.Source && e.ClientReference == reference).Select(e => new { e.Id, e.Number }).FirstAsync(ct);
            return new(dup.Id, dup.Number, true);
        }
        await m.IndexAsync(RecordType, x.Id, x.Description, ct);
        await tx.CommitAsync(ct);
        var triagers = await m.UsersWithAsync(Permissions.QualityOpsEdit, x.SiteId, x.DepartmentId, ct);
        await notifier.NotifyAsync(triagers.Where(u => u != x.ReportedById), $"Quality event {x.Number} reported", x.Title, await m.RecordAsync(RecordType, x.Id, ct), email: false, ct: ct);
        return new(x.Id, x.Number, false);
    }

    public async Task StartTriageAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != QualityEventStatus.New) throw new ConflictException("Only a new event can move to triage.");
        x.Status = QualityEventStatus.InTriage; x.TriagedById = m.UserId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Classifies the event and rates it on the severity matrix; the response time follows the severity.</summary>
    public async Task ClassifyAsync(Guid id, TriageInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (QualityEventStatus.New or QualityEventStatus.InTriage or QualityEventStatus.Classified)) throw new ConflictException("This event can no longer be classified.");
        var s = await GetSettingsAsync(ct);
        ModuleSupport.Required(r.Classification, "Classification", 100);
        if (!s.Classifications.Contains(r.Classification.Trim())) throw new AppException("Choose a configured classification.");
        var category = T(r.Category) ?? x.Category;
        if (category is null) throw new AppException("Choose an event category.");
        if (!s.Categories.Contains(category)) throw new AppException("Choose a configured event category.");
        var sev = Rate(s, r.Likelihood, r.Impact);
        x.Category = category; x.Classification = r.Classification.Trim(); x.Likelihood = r.Likelihood; x.Impact = r.Impact; x.Severity = sev.Name;
        x.ResponseHours = sev.ResponseHours; x.RespondBy = x.OccurredAt.AddHours(sev.ResponseHours);
        x.TriageNotes = T(r.Notes); x.TriagedById = m.UserId; x.TriagedAt = m.Now; x.Status = QualityEventStatus.Classified;
        await db.SaveChangesAsync(ct);
        if (sev.Escalate)
        {
            var who = await m.UsersWithAsync(Permissions.QualityOpsApprove, x.SiteId, x.DepartmentId, ct);
            await notifier.NotifyAsync(who, $"{sev.Name} quality event {x.Number}", $"{x.Title}: respond by {x.RespondBy:yyyy-MM-dd HH:mm} UTC.", await m.RecordAsync(RecordType, x.Id, ct), ct: ct);
        }
    }

    /// <summary>
    /// Escalates a classified event: "deviation", "investigation" and "complaint" create the record; "record" links an existing module record
    /// (nonconformance, corrective action, complaint or any other registered type) the user may view.
    /// </summary>
    public async Task<(string Type, Guid Id, string Number)> EscalateAsync(Guid id, EscalateInput r, DeviationService deviations, InvestigationService investigations, ComplaintService complaints, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != QualityEventStatus.Classified) throw new ConflictException("Classify the event before escalating it.");
        (string Type, Guid Id, string Number) target;
        switch ((r.Target ?? "").Trim().ToLowerInvariant())
        {
            case "deviation":
                var d = await deviations.CreateAsync(new DeviationInput(x.SiteId, x.DepartmentId, DeviationKind.Unplanned, T(r.Title) ?? x.Title, "To be stated", x.Description, "To be stated",
                    null, null, x.PartId, x.ProductId, x.SupplierId, x.CustomerId, x.LotNumber, null, null, null, false, r.OwnerId, x.Id), ct);
                target = (DeviationService.RecordType, d.Id, d.Number);
                break;
            case "investigation":
                var i = await investigations.CreateAsync(new InvestigationInput(x.SiteId, x.DepartmentId,
                    x.Classification == "Laboratory result" ? InvestigationKind.Laboratory : InvestigationKind.CrossModule, T(r.Title) ?? x.Title, x.Description, r.OwnerId, null, x.Id), ct);
                target = (InvestigationService.RecordType, i.Id, i.Number);
                break;
            case "complaint":
                // complaint intake automation: an event from a customer becomes a customer complaint with the event's facts
                if (x.CustomerId is not { } customerId) throw new AppException("A complaint needs the customer; record the customer on the event first.");
                var today = await m.TodayAsync(x.SiteId, ct);
                var settings = await GetSettingsAsync(ct);
                var order = settings.Severities.OrderBy(v => v.Order).Select(v => v.Name).ToList();
                var rank = x.Severity is null ? -1 : order.IndexOf(x.Severity);
                var sev = rank < 0 ? Severity.Minor : rank == order.Count - 1 ? Severity.Critical : rank == order.Count - 2 ? Severity.Major : Severity.Minor;
                var dueDays = x.RespondBy is { } rb ? Math.Max(0, (int)Math.Ceiling((rb - m.Now).TotalDays)) : 0;
                var occurred = DateOnly.FromDateTime(x.OccurredAt.UtcDateTime);
                var c = await complaints.CreateAsync(new ComplaintRequest(T(r.Title) ?? x.Title, x.Description, customerId, null, x.ClientReference, occurred > today ? today : occurred, today,
                    x.ProductId, x.PartId, null, null, x.LotNumber, null, null, null, x.Classification, sev, sev, WarrantyStatus.Unknown, x.SupplierId, x.SiteId, x.DepartmentId, r.OwnerId,
                    today.AddDays(dueDays)), ct);
                target = (ComplaintService.RecordType, c.Id, c.Number);
                break;
            case "record":
                if (string.IsNullOrWhiteSpace(r.RecordType) || r.RecordId is null) throw new AppException("Choose the record to escalate to.");
                if (!registry.IsKnownType(r.RecordType)) throw new AppException("Unknown record type.");
                var info = await registry.RequireViewAsync(r.RecordType, r.RecordId.Value, ct);
                target = (info.Type, info.Id, info.Number);
                break;
            default: throw new AppException("Escalate to a deviation, an investigation, a complaint or an existing record.");
        }
        x = await db.QualityEvents.FirstAsync(e => e.Id == id, ct);
        x.Status = QualityEventStatus.Escalated; x.EscalatedToType = target.Type; x.EscalatedToId = target.Id; x.EscalatedToNumber = target.Number;
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = x.Id, ToType = target.Type, ToId = target.Id, LinkType = "Escalated to" });
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task CloseAsync(Guid id, string notes, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is QualityEventStatus.Closed or QualityEventStatus.Rejected) throw new ConflictException("This event is already closed.");
        if (x.Status == QualityEventStatus.New) throw new ConflictException("Triage the event before closing it.");
        ModuleSupport.Required(notes, "Closure notes", 4000);
        x.Status = QualityEventStatus.Closed; x.ClosureNotes = notes.Trim(); x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task RejectAsync(Guid id, string reason, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (QualityEventStatus.New or QualityEventStatus.InTriage)) throw new ConflictException("Only an event in intake or triage can be rejected.");
        ModuleSupport.Required(reason, "Reason", 4000);
        x.Status = QualityEventStatus.Rejected; x.ClosureNotes = "Not a quality event: " + reason.Trim(); x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    private async Task RefsAsync(Guid? part, Guid? product, Guid? supplier, Guid? customer, CancellationToken ct)
    {
        if (part is { } p && !await db.Parts.AnyAsync(e => e.Id == p, ct)) throw new AppException("The part does not exist.");
        if (product is { } pr && !await db.Products.AnyAsync(e => e.Id == pr, ct)) throw new AppException("The product does not exist.");
        if (supplier is { } s && !await db.Suppliers.AnyAsync(e => e.Id == s, ct)) throw new AppException("The supplier does not exist.");
        if (customer is { } c && !await db.Customers.AnyAsync(e => e.Id == c, ct)) throw new AppException("The customer does not exist.");
    }

    private async Task<QualityEvent> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.QualityEvents.FirstAsync(x => x.Id == id, ct);
    }

    internal static List<TItem> L<TItem>(string json) => JsonSerializer.Deserialize<List<TItem>>(json, Json) ?? [];
    internal static string J<TItem>(IEnumerable<TItem> v) => JsonSerializer.Serialize(v, Json);
    internal static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
