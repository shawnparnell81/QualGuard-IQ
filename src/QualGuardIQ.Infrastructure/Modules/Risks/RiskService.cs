using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Risks;

public sealed record RiskLevel(int Value, string Label);
public sealed record RiskBand(int MinScore, string Label, string Color, bool RequiresAcceptance, bool Escalate);
public sealed record RiskSettingsDto(List<RiskLevel> Probability, List<RiskLevel> Severity, List<RiskLevel> Detectability, bool UseDetectability, List<RiskBand> Bands, List<string> Categories, uint RowVersion = 0);
public sealed record RiskInput(RiskKind Kind, string Category, string? Source, string Title, string Description, string? Cause, string? Consequence, string? Process, Guid? ProductId,
    Guid SiteId, Guid? DepartmentId, Guid? OwnerId, int Probability, int Severity, int? Detectability, string? ExistingControls, string? MitigationPlan, string? AcceptanceCriteria,
    int ReviewIntervalDays = 180, string? Monitoring = null, uint RowVersion = 0);
public sealed record ResidualInput(int Probability, int Severity, int? Detectability, string AcceptanceCriteria);
public sealed record RiskReviewInput(string Notes, bool StillEffective, int? Probability, int? Severity, int? Detectability);

/// <summary>
/// Risk and opportunity register. Scores and bands come from the configured matrix (RiskSettings), never hard-coded.
/// Lifecycle: Open → Mitigating (plan + actions) → residual assessment → PendingAcceptance (when the residual band
/// requires acceptance; approved through the workflow engine) → Monitoring (periodic reviews) → Closed.
/// </summary>
public sealed class RiskService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, ActionService actions, INotifier notifier)
{
    public const string RecordType = "Risk";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly RiskSettingsDto Defaults = new(
        [new(1, "Rare"), new(2, "Unlikely"), new(3, "Possible"), new(4, "Likely"), new(5, "Almost certain")],
        [new(1, "Negligible"), new(2, "Minor"), new(3, "Moderate"), new(4, "Major"), new(5, "Severe")],
        [new(1, "Certain to detect"), new(2, "High"), new(3, "Moderate"), new(4, "Low"), new(5, "Undetectable")],
        false,
        [new(1, "Low", "#2e7d32", false, false), new(5, "Medium", "#f9a825", false, false), new(10, "High", "#ef6c00", true, false), new(17, "Critical", "#c62828", true, true)],
        ["Organizational", "Process", "Product", "Project", "Supplier", "Customer", "Equipment", "Quality", "Operational", "Change", "System", "Improvement opportunity"]);

    public async Task<RiskSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var s = await db.RiskSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return s is null ? Defaults : new(L<RiskLevel>(s.ProbabilityLevels), L<RiskLevel>(s.SeverityLevels), L<RiskLevel>(s.DetectabilityLevels), s.UseDetectability, L<RiskBand>(s.Bands), L<string>(s.Categories), s.RowVersion);
    }

    public async Task SaveSettingsAsync(RiskSettingsDto r, CancellationToken ct)
    {
        Validate(r);
        var s = await db.RiskSettings.FirstOrDefaultAsync(ct);
        if (s is null) db.RiskSettings.Add(s = new RiskSettings());
        else db.Entry(s).Property(x => x.RowVersion).OriginalValue = r.RowVersion;
        s.ProbabilityLevels = J(r.Probability.OrderBy(x => x.Value)); s.SeverityLevels = J(r.Severity.OrderBy(x => x.Value)); s.DetectabilityLevels = J(r.Detectability.OrderBy(x => x.Value));
        s.UseDetectability = r.UseDetectability; s.Bands = J(r.Bands.OrderBy(x => x.MinScore)); s.Categories = J(r.Categories.Select(x => x.Trim()).Distinct());
        await db.SaveChangesAsync(ct);
    }

    private static void Validate(RiskSettingsDto r)
    {
        void Scale(List<RiskLevel> l, string name)
        {
            if (l.Count is < 2 or > 10) throw new AppException($"The {name} scale needs 2 to 10 levels.");
            if (l.Any(x => x.Value < 1) || l.Select(x => x.Value).Distinct().Count() != l.Count) throw new AppException($"{name} values must be unique positive numbers.");
            if (l.Any(x => string.IsNullOrWhiteSpace(x.Label))) throw new AppException($"Every {name} level needs a label.");
        }
        Scale(r.Probability, "probability"); Scale(r.Severity, "severity");
        if (r.UseDetectability) Scale(r.Detectability, "detectability");
        if (r.Bands.Count == 0 || r.Bands.Min(b => b.MinScore) > 1) throw new AppException("Score bands must cover every score starting at 1.");
        if (r.Bands.Select(b => b.MinScore).Distinct().Count() != r.Bands.Count) throw new AppException("Each band needs a different minimum score.");
        if (r.Bands.Any(b => string.IsNullOrWhiteSpace(b.Label) || !System.Text.RegularExpressions.Regex.IsMatch(b.Color ?? "", "^#[0-9a-fA-F]{6}$"))) throw new AppException("Each band needs a label and a #RRGGBB color.");
        if (r.Categories.Count == 0 || r.Categories.Any(string.IsNullOrWhiteSpace)) throw new AppException("Define at least one category.");
    }

    /// <summary>Score = probability × severity (× detectability when configured); band = highest band whose minimum is met.</summary>
    public static (int Score, RiskBand Band) Rate(RiskSettingsDto s, int p, int sev, int? d)
    {
        if (!s.Probability.Any(x => x.Value == p)) throw new AppException("Choose a probability from the configured scale.");
        if (!s.Severity.Any(x => x.Value == sev)) throw new AppException("Choose a severity from the configured scale.");
        var score = p * sev;
        if (s.UseDetectability)
        {
            if (d is null || !s.Detectability.Any(x => x.Value == d)) throw new AppException("Choose a detectability from the configured scale.");
            score *= d.Value;
        }
        return (score, s.Bands.OrderByDescending(b => b.MinScore).First(b => score >= b.MinScore));
    }

    public async Task<RiskItem> CreateAsync(RiskInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.RisksEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var x = new RiskItem { SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, Status = RiskStatus.Open, Number = await numbers.NextAsync(RecordType, r.SiteId, ct) };
        await ApplyAsync(x, r, ct);
        x.ReviewDueOn = (await m.TodayAsync(r.SiteId, ct)).AddDays(x.ReviewIntervalDays);
        db.Risks.Add(x);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, x.Description, ct);
        await EscalateAsync(x, ct);
        await tx.CommitAsync(ct);
        return x;
    }

    public async Task UpdateAsync(Guid id, RiskInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is RiskStatus.Closed or RiskStatus.Cancelled or RiskStatus.PendingAcceptance) throw new ConflictException("This risk cannot be edited in its current state.");
        if (r.SiteId != x.SiteId) throw new AppException("The site of a risk cannot change.");
        db.Entry(x).Property(e => e.RowVersion).OriginalValue = r.RowVersion;
        var wasEscalated = x.Escalated;
        await ApplyAsync(x, r, ct);
        if (r.OwnerId is { } o && o != x.OwnerId) { await m.RequireActiveUserAsync(o, "owner", ct); x.OwnerId = o; }
        x.DepartmentId = r.DepartmentId;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, x.Id, x.Description, ct);
        if (!wasEscalated) await EscalateAsync(x, ct);
    }

    private async Task ApplyAsync(RiskItem x, RiskInput r, CancellationToken ct)
    {
        var s = await GetSettingsAsync(ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Description, "Description", 10000);
        ModuleSupport.Required(r.Category, "Category", 100);
        if (!s.Categories.Contains(r.Category.Trim())) throw new AppException("Choose a configured category.");
        if (r.ReviewIntervalDays is < 1 or > 1825) throw new AppException("The review interval must be between 1 and 1825 days.");
        if (r.ProductId is { } p && !await db.Products.AnyAsync(e => e.Id == p, ct)) throw new AppException("The product does not exist.");
        var (score, band) = Rate(s, r.Probability, r.Severity, r.Detectability);
        x.Kind = r.Kind; x.Category = r.Category.Trim(); x.Source = T(r.Source); x.Title = r.Title.Trim(); x.Description = r.Description.Trim(); x.Cause = T(r.Cause); x.Consequence = T(r.Consequence);
        x.Process = T(r.Process); x.ProductId = r.ProductId; x.Probability = r.Probability; x.Severity = r.Severity; x.Detectability = s.UseDetectability ? r.Detectability : null;
        x.InitialScore = score; x.InitialBand = band.Label; x.ExistingControls = T(r.ExistingControls); x.MitigationPlan = T(r.MitigationPlan); x.AcceptanceCriteria = T(r.AcceptanceCriteria);
        x.ReviewIntervalDays = r.ReviewIntervalDays; x.Monitoring = T(r.Monitoring);
        if (band.Escalate) x.Escalated = true;
    }

    private async Task EscalateAsync(RiskItem x, CancellationToken ct)
    {
        if (!x.Escalated) return;
        await notifier.NotifyAsync([x.OwnerId], $"Risk {x.Number} escalated", $"{x.Title}: rated {x.ResidualBand ?? x.InitialBand}.", await m.RecordAsync(RecordType, x.Id, ct), ct: ct);
    }

    public async Task StartMitigationAsync(Guid id, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status != RiskStatus.Open) throw new ConflictException("Only an open risk can move to mitigation.");
        if (x.MitigationPlan is null) throw new AppException("Write the mitigation plan first.");
        x.Status = RiskStatus.Mitigating;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Records the residual rating once mitigation actions are done; high residual bands go to acceptance approval.</summary>
    public async Task AssessResidualAsync(Guid id, ResidualInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is not (RiskStatus.Open or RiskStatus.Mitigating or RiskStatus.Monitoring)) throw new ConflictException("The residual risk cannot be assessed in this state.");
        if (!await actions.AllClosedAsync(RecordType, id, ct)) throw new AppException("Complete all mitigation actions before assessing residual risk.");
        ModuleSupport.Required(r.AcceptanceCriteria, "Acceptance criteria", 4000);
        var (score, band) = Rate(await GetSettingsAsync(ct), r.Probability, r.Severity, r.Detectability);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        x.ResidualProbability = r.Probability; x.ResidualSeverity = r.Severity; x.ResidualDetectability = r.Detectability; x.ResidualScore = score; x.ResidualBand = band.Label;
        x.AcceptanceCriteria = r.AcceptanceCriteria.Trim(); x.AcceptedAt = null; x.AcceptedById = null;
        x.Escalated = band.Escalate;
        x.Status = band.RequiresAcceptance ? RiskStatus.PendingAcceptance : RiskStatus.Monitoring;
        await db.SaveChangesAsync(ct);
        if (band.RequiresAcceptance) await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
        await EscalateAsync(x, ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var x = await db.Risks.FirstAsync(e => e.Id == id, ct);
        x.Status = RiskStatus.Monitoring; x.AcceptedAt = m.Now; x.AcceptedById = m.UserId;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.OwnerId], $"Residual risk {x.Number} accepted", x.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var x = await db.Risks.FirstAsync(e => e.Id == id, ct);
        x.Status = RiskStatus.Mitigating;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([x.OwnerId], $"Residual risk {x.Number} not accepted", comment ?? "Further mitigation is required.", await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task<RiskReview> ReviewAsync(Guid id, RiskReviewInput r, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is RiskStatus.Closed or RiskStatus.Cancelled or RiskStatus.PendingAcceptance) throw new ConflictException("This risk cannot be reviewed in its current state.");
        ModuleSupport.Required(r.Notes, "Review notes", 4000);
        var today = await m.TodayAsync(x.SiteId, ct);
        int score; string band;
        if (r.Probability is { } p && r.Severity is { } sv)
        {
            var rated = Rate(await GetSettingsAsync(ct), p, sv, r.Detectability);
            score = rated.Score; band = rated.Band.Label;
            if (x.ResidualScore is null) { x.Probability = p; x.Severity = sv; x.Detectability = r.Detectability; x.InitialScore = score; x.InitialBand = band; }
            else { x.ResidualProbability = p; x.ResidualSeverity = sv; x.ResidualDetectability = r.Detectability; x.ResidualScore = score; x.ResidualBand = band; }
            if (rated.Band.Escalate && !x.Escalated) { x.Escalated = true; }
        }
        else { score = x.ResidualScore ?? x.InitialScore; band = x.ResidualBand ?? x.InitialBand; }
        var rev = new RiskReview { RiskId = id, ReviewedOn = today, ReviewedById = m.UserId, Score = score, Band = band, Notes = r.Notes.Trim(), StillEffective = r.StillEffective };
        db.RiskReviews.Add(rev);
        x.LastReviewedOn = today; x.ReviewDueOn = today.AddDays(x.ReviewIntervalDays);
        if (!r.StillEffective && x.Status == RiskStatus.Monitoring) x.Status = RiskStatus.Mitigating;
        await db.SaveChangesAsync(ct);
        await EscalateAsync(x, ct);
        return rev;
    }

    public async Task CloseAsync(Guid id, string notes, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is RiskStatus.Closed or RiskStatus.Cancelled or RiskStatus.PendingAcceptance) throw new ConflictException("This risk cannot be closed in its current state.");
        ModuleSupport.Required(notes, "Closure notes", 4000);
        if (!await actions.AllClosedAsync(RecordType, id, ct)) throw new AppException("All actions on this risk must be completed before closing.");
        x.Status = RiskStatus.Closed; x.ClosureNotes = notes.Trim(); x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelAsync(Guid id, string reason, CancellationToken ct)
    {
        var x = await LoadEditAsync(id, ct);
        if (x.Status is RiskStatus.Closed or RiskStatus.Cancelled or RiskStatus.PendingAcceptance) throw new ConflictException("This risk cannot be cancelled in its current state.");
        ModuleSupport.Required(reason, "Reason", 4000);
        x.Status = RiskStatus.Cancelled; x.ClosureNotes = "Cancelled: " + reason.Trim(); x.ClosedAt = m.Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task ReopenAsync(Guid id, string reason, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var x = await db.Risks.FirstAsync(e => e.Id == id, ct);
        await m.RequireAsync(Permissions.RisksApprove, x.SiteId, x.DepartmentId, ct);
        if (x.Status is not (RiskStatus.Closed or RiskStatus.Cancelled)) throw new ConflictException("Only a closed risk can be reopened.");
        ModuleSupport.Required(reason, "Reason", 4000);
        x.Status = RiskStatus.Open; x.ClosedAt = null; x.ClosureNotes = "Reopened: " + reason.Trim();
        await db.SaveChangesAsync(ct);
    }

    private async Task<RiskItem> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.Risks.FirstAsync(x => x.Id == id, ct);
    }

    private static List<TItem> L<TItem>(string json) => JsonSerializer.Deserialize<List<TItem>>(json, Json) ?? [];
    private static string J<TItem>(IEnumerable<TItem> v) => JsonSerializer.Serialize(v, Json);
    private static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
