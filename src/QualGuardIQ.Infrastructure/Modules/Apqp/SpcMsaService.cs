using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Apqp;

public sealed record SpcChartInput(string Title, SpcChartType ChartType, string Characteristic, string? Unit, int SubgroupSize, decimal? Lsl, decimal? Usl, decimal? Target,
    Guid? PartId, Guid? EquipmentId, Guid? ControlPlanId, bool AutoNcr, Guid SiteId, Guid? DepartmentId, Guid? OwnerId);
public sealed record SpcSampleInput(List<decimal>? Values, int? Count, int? Units, DateTimeOffset? TakenAt, Guid? EquipmentId, string? Notes);
public sealed record SpcSampleOutcome(SpcSample Sample, List<string> Violations, Guid? NcrId, string? NcrNumber, string? NcrSkippedReason);

public sealed record MsaStudyInput(string Title, MsaStudyType StudyType, Guid EquipmentId, string? InspectionMethod, string? Characteristic, decimal? Tolerance, decimal? ReferenceValue,
    int ReviewIntervalMonths, Guid SiteId, Guid? DepartmentId, Guid? OwnerId);
public sealed record MsaMeasurementInput(string? Operator, int Part, int Trial, decimal? Value, string? Attribute, decimal? Reference, string? AttributeReference);

/// <summary>SPC charts: samples are evaluated against all chart data; rule violations notify SPC users and can raise an NCR.</summary>
public sealed class SpcService(AppDbContext db, ModuleSupport m, INumberingService numbers, INotifier notifier, IAccessService access, NcrService ncrs)
{
    public const string RecordType = "SpcChart";
    public const int Window = 125;

    async Task<SpcChart> LoadAsync(Guid id, bool edit, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var c = await db.SpcCharts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("SPC chart");
        if (edit) await m.RequireAsync(Permissions.SpcEdit, c.SiteId, c.DepartmentId, ct);
        return c;
    }

    static bool IsVariables(SpcChartType t) => t is SpcChartType.XBarR or SpcChartType.XBarS or SpcChartType.IndividualsMR;

    async Task ValidateAsync(SpcChartInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        ModuleSupport.Required(r.Characteristic, "Characteristic", 300);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.SpcEdit, r.SiteId, r.DepartmentId, ct);
        var n = r.SubgroupSize;
        if (r.ChartType == SpcChartType.IndividualsMR && n != 1) throw new AppException("Individuals / moving range charts use subgroups of 1.");
        if (r.ChartType == SpcChartType.XBarR && n is < 2 or > 10) throw new AppException("X̄-R charts need subgroups of 2 to 10.");
        if (r.ChartType == SpcChartType.XBarS && n is < 2 or > 25) throw new AppException("X̄-S charts need subgroups of 2 to 25.");
        if (!IsVariables(r.ChartType) && n < 1) throw new AppException("Sample size must be at least 1.");
        if (r.Lsl is { } l && r.Usl is { } u && l >= u) throw new AppException("The lower spec limit must be below the upper spec limit.");
        if (r.PartId is { } p && !await db.Parts.AnyAsync(x => x.Id == p, ct)) throw new AppException("Select an existing part.");
        if (r.EquipmentId is { } e && !await db.Equipment.AnyAsync(x => x.Id == e, ct)) throw new AppException("Select existing equipment.");
        if (r.ControlPlanId is { } cp && !await db.ControlPlans.AnyAsync(x => x.Id == cp, ct)) throw new AppException("Select an existing control plan.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
    }

    public async Task<SpcChart> CreateAsync(SpcChartInput r, CancellationToken ct)
    {
        await ValidateAsync(r, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new SpcChart { OwnerId = r.OwnerId ?? m.UserId, Number = await numbers.NextAsync(RecordType, r.SiteId, ct) };
        Apply(c, r);
        db.SpcCharts.Add(c);
        if (r.ControlPlanId is { } cp) db.RecordLinks.Add(new RecordLink { FromType = ApqpService.PlanType, FromId = cp, ToType = RecordType, ToId = c.Id, LinkType = "Monitored by" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, $"{c.Title} {c.Characteristic}", ct);
        await tx.CommitAsync(ct);
        return c;
    }

    static void Apply(SpcChart c, SpcChartInput r)
    {
        c.Title = r.Title.Trim(); c.ChartType = (int)r.ChartType; c.Characteristic = r.Characteristic.Trim(); c.Unit = r.Unit; c.SubgroupSize = r.SubgroupSize;
        c.Lsl = r.Lsl; c.Usl = r.Usl; c.Target = r.Target; c.PartId = r.PartId; c.EquipmentId = r.EquipmentId; c.ControlPlanId = r.ControlPlanId; c.AutoNcr = r.AutoNcr;
        c.SiteId = r.SiteId; c.DepartmentId = r.DepartmentId; if (r.OwnerId is { } o) c.OwnerId = o;
    }

    public async Task UpdateAsync(Guid id, SpcChartInput r, CancellationToken ct)
    {
        var c = await LoadAsync(id, true, ct);
        if ((int)r.ChartType != c.ChartType || r.SubgroupSize != c.SubgroupSize)
            if (await db.SpcSamples.AnyAsync(s => s.ChartId == id, ct)) throw new ConflictException("Chart type and subgroup size cannot change once samples are recorded.");
        await ValidateAsync(r, ct);
        Apply(c, r);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, c.Id, $"{c.Title} {c.Characteristic}", ct);
    }

    public async Task<List<SpcChart>> ListAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.SpcCharts.AsNoTracking(), Permissions.SpcView, ct)).OrderBy(x => x.Number).Take(500).ToListAsync(ct);

    public async Task<(SpcChart Chart, List<SpcSample> Samples, SpcResult? Result)> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, false, ct);
        var samples = await Recent(id, ct);
        return (c, samples, samples.Count == 0 ? null : Compute(c, samples));
    }

    async Task<List<SpcSample>> Recent(Guid id, CancellationToken ct) =>
        (await db.SpcSamples.AsNoTracking().Where(s => s.ChartId == id).OrderByDescending(s => s.TakenAt).ThenByDescending(s => s.CreatedAt).Take(Window).ToListAsync(ct))
        .OrderBy(s => s.TakenAt).ThenBy(s => s.CreatedAt).ToList();

    public static SpcResult Compute(SpcChart c, List<SpcSample> samples) =>
        SpcMath.Compute((SpcChartType)c.ChartType, samples.Select(s => new SpcSubgroup(JsonSerializer.Deserialize<List<decimal>>(s.Values) ?? [], s.Count, s.Units)).ToList(), c.Lsl, c.Usl);

    public async Task<SpcSampleOutcome> AddSampleAsync(Guid id, SpcSampleInput r, CancellationToken ct)
    {
        var c = await LoadAsync(id, true, ct);
        if (c.IsArchived) throw new ConflictException("The chart is archived.");
        var type = (SpcChartType)c.ChartType;
        var values = r.Values ?? [];
        if (IsVariables(type))
        {
            if (values.Count != c.SubgroupSize) throw new AppException($"Enter exactly {c.SubgroupSize} measurement(s) for this chart.");
        }
        else
        {
            if (r.Count is null or < 0) throw new AppException(type is SpcChartType.P or SpcChartType.NP ? "Enter the number of defective units." : "Enter the number of defects.");
            var units = r.Units ?? c.SubgroupSize;
            if (units < 1) throw new AppException("Sample size must be at least 1.");
            if (type is SpcChartType.P or SpcChartType.NP && r.Count > units) throw new AppException("Defectives cannot exceed the sample size.");
            if (type == SpcChartType.NP && units != c.SubgroupSize) throw new AppException($"NP charts use a constant sample size of {c.SubgroupSize}.");
        }
        if (r.EquipmentId is { } e && !await db.Equipment.AnyAsync(x => x.Id == e, ct)) throw new AppException("Select existing equipment.");
        if (r.TakenAt is { } t && t > m.Now.AddMinutes(5)) throw new AppException("The sample time cannot be in the future.");
        var s = new SpcSample
        {
            ChartId = id, TakenAt = r.TakenAt ?? m.Now, OperatorId = m.UserId, EquipmentId = r.EquipmentId ?? c.EquipmentId, Values = JsonSerializer.Serialize(values),
            Count = IsVariables(type) ? null : r.Count, Units = IsVariables(type) ? null : r.Units ?? c.SubgroupSize, Notes = r.Notes
        };
        var all = await Recent(id, ct);
        all.Add(s);
        all = all.OrderBy(x => x.TakenAt).ToList();
        var result = Compute(c, all);
        var idx = all.IndexOf(s);
        var hits = result.Points[idx].Violations;
        s.Violations = hits.Count == 0 ? null : string.Join("; ", hits);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.SpcSamples.Add(s);
        await db.SaveChangesAsync(ct);
        Guid? ncrId = null; string? ncrNumber = null, skipped = null;
        if (hits.Count > 0)
        {
            if (c.AutoNcr)
            {
                if (await m.CanAsync(Permissions.NcrEdit, c.SiteId, c.DepartmentId, ct))
                {
                    var part = c.PartId is { } pid ? await db.Parts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == pid, ct) : null;
                    var ncr = await ncrs.CreateAsync(new CreateNcrRequest($"SPC out of control: {c.Characteristic} ({c.Number})",
                        $"Sample at {s.TakenAt:u} on chart {c.Number} \"{c.Title}\" violated: {s.Violations}.", "Process", c.SiteId, c.DepartmentId, c.OwnerId,
                        part?.PartNumber, null, null, await m.TodayAsync(c.SiteId, ct), Severity.Major, new NcrDetails(PartId: c.PartId, ProductId: part?.ProductId, DetectionPoint: "SPC")), ct);
                    s.NcrId = ncr.Id; ncrId = ncr.Id; ncrNumber = ncr.Number;
                    db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = c.Id, ToType = NcrService.RecordType, ToId = ncr.Id, LinkType = "Out-of-control NCR" });
                    await db.SaveChangesAsync(ct);
                }
                else skipped = "You do not have NCR edit permission for this site, so no NCR was raised; SPC users were notified.";
            }
            var recipients = (await access.UsersWithPermissionAsync(Permissions.SpcEdit, c.SiteId, c.DepartmentId, ct)).Append(c.OwnerId).Distinct().ToList();
            await notifier.NotifyAsync(recipients, $"SPC {c.Number} out of control", $"{c.Characteristic}: {s.Violations}" + (ncrNumber is null ? "" : $" NCR {ncrNumber} raised."), await m.RecordAsync(RecordType, c.Id, ct), ct: ct);
        }
        await tx.CommitAsync(ct);
        return new(s, hits, ncrId, ncrNumber, skipped);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var c = await LoadAsync(id, true, ct);
        c.IsArchived = true;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>MSA studies (bias, linearity, stability, variable and attribute gage R&amp;R) with approval and review cycles.</summary>
public sealed class MsaService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, INotifier notifier)
{
    public const string RecordType = "MsaStudy";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    async Task<MsaStudy> LoadAsync(Guid id, bool edit, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var s = await db.MsaStudies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("MSA study");
        if (edit)
        {
            await m.RequireAsync(Permissions.SpcEdit, s.SiteId, s.DepartmentId, ct);
            if (s.Status is not (MsaStatus.Draft or MsaStatus.Rejected)) throw new ConflictException($"{s.Number} is {s.Status} and cannot change.");
        }
        return s;
    }

    public async Task<MsaStudy> CreateAsync(MsaStudyInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 300);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        await m.RequireAsync(Permissions.SpcEdit, r.SiteId, r.DepartmentId, ct);
        if (!await db.Equipment.AnyAsync(x => x.Id == r.EquipmentId, ct)) throw new AppException("Select the gage (equipment) under study.");
        if (r.StudyType == MsaStudyType.Bias && r.ReferenceValue is null) throw new AppException("A bias study needs the reference value.");
        if (r.Tolerance is <= 0) throw new AppException("Tolerance must be positive.");
        if (r.ReviewIntervalMonths is < 1 or > 60) throw new AppException("Review interval must be 1 to 60 months.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = new MsaStudy
        {
            Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Title = r.Title.Trim(), StudyType = r.StudyType, EquipmentId = r.EquipmentId, InspectionMethod = r.InspectionMethod,
            Characteristic = r.Characteristic, Tolerance = r.Tolerance, ReferenceValue = r.ReferenceValue, ReviewIntervalMonths = r.ReviewIntervalMonths, Status = MsaStatus.Draft,
            SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = r.OwnerId ?? m.UserId
        };
        db.MsaStudies.Add(s);
        db.RecordLinks.Add(new RecordLink { FromType = "Equipment", FromId = r.EquipmentId, ToType = RecordType, ToId = s.Id, LinkType = "Measurement system study" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, s.Id, $"{s.Title} {s.StudyType}", ct);
        await tx.CommitAsync(ct);
        return s;
    }

    public async Task SaveMeasurementsAsync(Guid id, List<MsaMeasurementInput> rows, CancellationToken ct)
    {
        var s = await LoadAsync(id, true, ct);
        var attr = s.StudyType == MsaStudyType.AttributeGrr;
        foreach (var r in rows)
        {
            if (attr ? string.IsNullOrWhiteSpace(r.Attribute) : r.Value is null) throw new AppException(attr ? "Each attribute result needs a decision." : "Each measurement needs a value.");
            if (r.Part < 1 || r.Trial < 1) throw new AppException("Part and trial numbers start at 1.");
            if (s.StudyType is MsaStudyType.VariableGrr or MsaStudyType.AttributeGrr && string.IsNullOrWhiteSpace(r.Operator)) throw new AppException("Gage R&R results need the operator.");
            if (s.StudyType == MsaStudyType.Linearity && r.Reference is null) throw new AppException("Linearity readings need the reference value of the part.");
        }
        db.MsaMeasurements.RemoveRange(db.MsaMeasurements.Where(x => x.StudyId == id));
        db.MsaMeasurements.AddRange(rows.Select(r => new MsaMeasurement { StudyId = id, Operator = (r.Operator ?? "").Trim(), Part = r.Part, Trial = r.Trial, Value = r.Value, Attribute = r.Attribute?.Trim(), Reference = r.Reference, AttributeReference = r.AttributeReference?.Trim() }));
        s.Result = null; s.Verdict = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<MsaMeasurement>> MeasurementsAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        return await db.MsaMeasurements.AsNoTracking().Where(x => x.StudyId == id).OrderBy(x => x.Operator).ThenBy(x => x.Part).ThenBy(x => x.Trial).ToListAsync(ct);
    }

    /// <summary>Computes the study result from the stored measurements (does not save).</summary>
    public async Task<(object Result, string Verdict)> ComputeAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var s = await db.MsaStudies.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var rows = await db.MsaMeasurements.AsNoTracking().Where(x => x.StudyId == id).ToListAsync(ct);
        if (rows.Count == 0) throw new AppException("Enter measurements first.");
        try
        {
            switch (s.StudyType)
            {
                case MsaStudyType.VariableGrr:
                    var g = MsaMath.VariableGrr(rows.Select(r => new GrrMeasurement(r.Operator, r.Part, r.Trial, r.Value!.Value)).ToList(), s.Tolerance);
                    return (g, g.Verdict);
                case MsaStudyType.AttributeGrr:
                    var a = MsaMath.Attribute(rows.Select(r => new AttributeMeasurement(r.Operator, r.Part, r.Trial, r.Attribute!, r.AttributeReference)).ToList());
                    return (a, a.Verdict);
                case MsaStudyType.Bias:
                    var b = MsaMath.Bias(rows.Select(r => r.Value!.Value).ToList(), s.ReferenceValue!.Value, s.Tolerance);
                    var bv = b.Significant ? (b.PctTolerance is { } pt && pt <= 10 ? "Marginal" : "Unacceptable") : "Acceptable";
                    return (b, bv);
                case MsaStudyType.Linearity:
                    var l = MsaMath.Linearity(rows.Select(r => (r.Reference!.Value, r.Value!.Value)).ToList());
                    var lv = l.PctLinearity <= 5 ? "Acceptable" : l.PctLinearity <= 10 ? "Marginal" : "Unacceptable";
                    return (new { l.Slope, l.Intercept, l.RSquared, l.PctLinearity, Points = l.Points.Select(p => new { p.Reference, p.MeanBias }) }, lv);
                default:
                    var groups = rows.GroupBy(r => r.Trial).OrderBy(x => x.Key).Select(x => new SpcSubgroup(x.Select(r => r.Value!.Value).ToList())).ToList();
                    var size = groups.Select(x => x.Values.Count).Distinct().ToList();
                    if (size.Count != 1) throw new ArgumentException("Every stability subgroup (trial) needs the same number of readings.");
                    var chart = size[0] == 1 ? SpcChartType.IndividualsMR : SpcChartType.XBarR;
                    var res = SpcMath.Compute(chart, groups, null, null);
                    return (new { Chart = chart.ToString(), res.Center, res.Ucl, res.Lcl, res.RangeCenter, res.RangeUcl, res.Violations, Points = res.Points.Select(p => new { p.Index, p.Value, p.Violations }) },
                        res.Violations.Count == 0 ? "Acceptable" : "Unacceptable");
            }
        }
        catch (ArgumentException e) { throw new AppException(e.Message); }
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct)
    {
        var s = await LoadAsync(id, true, ct);
        var (result, verdict) = await ComputeAsync(id, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        s.Result = JsonSerializer.Serialize(result, result.GetType(), Json); s.Verdict = verdict; s.Status = MsaStatus.PendingApproval;
        await db.SaveChangesAsync(ct);
        await workflow.SubmitAsync(RecordType, id, ct);
        await tx.CommitAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var s = await db.MsaStudies.FirstAsync(x => x.Id == id, ct);
        if (s.Status != MsaStatus.PendingApproval) return;
        s.Status = MsaStatus.Approved; s.NextReviewOn = (await m.TodayAsync(s.SiteId, ct)).AddMonths(s.ReviewIntervalMonths);
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([s.OwnerId], $"MSA {s.Number} approved", $"{s.Title}: {s.Verdict}. Next review {s.NextReviewOn:yyyy-MM-dd}.", await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var s = await db.MsaStudies.FirstAsync(x => x.Id == id, ct);
        if (s.Status != MsaStatus.PendingApproval) return;
        s.Status = MsaStatus.Rejected;
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([s.OwnerId], $"MSA {s.Number} rejected", comment ?? "Study not approved.", await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task<List<MsaStudy>> ListAsync(Guid? equipmentId, bool dueOnly, CancellationToken ct)
    {
        var q = await m.ScopeAsync(db.MsaStudies.AsNoTracking(), Permissions.SpcView, ct);
        if (equipmentId is { } e) q = q.Where(x => x.EquipmentId == e);
        if (dueOnly)
        {
            var soon = DateOnly.FromDateTime(m.Now.UtcDateTime).AddDays(30);
            q = q.Where(x => x.Status == MsaStatus.Approved && x.NextReviewOn <= soon);
        }
        return await q.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct);
    }

    public async Task<MsaStudy> GetAsync(Guid id, CancellationToken ct) { await m.ViewAsync(RecordType, id, ct); return await db.MsaStudies.AsNoTracking().FirstAsync(x => x.Id == id, ct); }

    /// <summary>Starts the next review cycle: a new draft study with the same setup, linked to the previous one.</summary>
    public async Task<MsaStudy> ReviewCycleAsync(Guid id, CancellationToken ct)
    {
        var s = await GetAsync(id, ct);
        if (s.Status != MsaStatus.Approved) throw new ConflictException("Only an approved study starts a review cycle.");
        var n = await CreateAsync(new MsaStudyInput(s.Title, s.StudyType, s.EquipmentId, s.InspectionMethod, s.Characteristic, s.Tolerance, s.ReferenceValue, s.ReviewIntervalMonths, s.SiteId, s.DepartmentId, null), ct);
        db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = id, ToType = RecordType, ToId = n.Id, LinkType = "Review cycle" });
        await db.SaveChangesAsync(ct);
        return n;
    }
}
