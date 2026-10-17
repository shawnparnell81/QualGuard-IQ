using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

public sealed record CreateEquipmentRequest(string Name, string? Manufacturer, string? Model, string? SerialNumber, string? Location, string? MeasurementRange,
    string? Tolerance, Guid SiteId, Guid? DepartmentId, Guid? OwnerId, int CalibrationIntervalDays, DateOnly? NextDueOn);
public sealed record RecordCalibrationRequest(DateOnly PerformedOn, string PerformedBy, CalibrationResult Result, string? AsFound, string? AsLeft, string? StandardsUsed, string? CertificateNumber);
public sealed record CompleteImpactRequest(string AffectedProduct, string Assessment, string Decision);

public sealed class CalibrationService(AppDbContext db, ModuleSupport m, INumberingService numbers, INotifier notifier, QualGuardIQ.Application.Security.IAccessService access, NcrService ncrs)
{
    public const string EquipmentType = "Equipment";
    public const string EventType = "CalibrationEvent";
    public const string ImpactType = "ImpactAssessment";

    public async Task<Equipment> CreateAsync(CreateEquipmentRequest r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.CalibrationEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Name, "Name", 200);
        if (r.CalibrationIntervalDays is < 1 or > 3650) throw new AppException("The calibration interval must be between 1 and 3650 days.");
        var owner = r.OwnerId ?? m.UserId;
        await m.RequireActiveUserAsync(owner, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var e = new Equipment
        {
            Number = await numbers.NextAsync(EquipmentType, r.SiteId, ct), Name = r.Name.Trim(), Manufacturer = r.Manufacturer?.Trim(), Model = r.Model?.Trim(),
            SerialNumber = r.SerialNumber?.Trim(), Location = r.Location?.Trim(), MeasurementRange = r.MeasurementRange?.Trim(), Tolerance = r.Tolerance?.Trim(),
            SiteId = r.SiteId, DepartmentId = r.DepartmentId, OwnerId = owner, CalibrationIntervalDays = r.CalibrationIntervalDays,
            NextDueOn = r.NextDueOn ?? m.Today, Status = EquipmentStatus.Active
        };
        db.Equipment.Add(e);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(EquipmentType, e.Id, $"{e.Manufacturer} {e.Model} {e.SerialNumber} {e.Location}", ct);
        await tx.CommitAsync(ct);
        return e;
    }

    /// <summary>Records a calibration. Events are immutable. Out-of-tolerance or failed results quarantine the equipment and open an impact assessment.</summary>
    public async Task<(CalibrationEvent Event, ImpactAssessment? Impact)> RecordAsync(Guid equipmentId, RecordCalibrationRequest r, CancellationToken ct)
    {
        await m.EditAsync(EquipmentType, equipmentId, ct);
        var e = await db.Equipment.FirstAsync(x => x.Id == equipmentId, ct);
        if (e.Status == EquipmentStatus.Retired) throw new ConflictException("Retired equipment cannot be calibrated.");
        ModuleSupport.Required(r.PerformedBy, "Performed by", 200);
        if (r.PerformedOn > m.Today) throw new AppException("The calibration date cannot be in the future.");
        if (e.LastCalibratedOn is { } last && r.PerformedOn < last) throw new AppException("A later calibration is already recorded.");
        var pass = r.Result is CalibrationResult.Pass or CalibrationResult.PassAfterAdjustment;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ev = new CalibrationEvent
        {
            Number = await numbers.NextAsync(EventType, e.SiteId, ct), EquipmentId = e.Id, SiteId = e.SiteId, DepartmentId = e.DepartmentId, PerformedOn = r.PerformedOn,
            PerformedBy = r.PerformedBy.Trim(), Result = r.Result, AsFound = r.AsFound?.Trim(), AsLeft = r.AsLeft?.Trim(), StandardsUsed = r.StandardsUsed?.Trim(),
            CertificateNumber = r.CertificateNumber?.Trim(), NextDueOn = pass ? r.PerformedOn.AddDays(e.CalibrationIntervalDays) : null
        };
        db.CalibrationEvents.Add(ev);
        ImpactAssessment? ia = null;
        var lastGood = e.LastCalibratedOn;
        if (pass)
        {
            e.LastCalibratedOn = r.PerformedOn;
            e.NextDueOn = ev.NextDueOn;
        }
        else
        {
            e.Status = EquipmentStatus.Quarantined;
            ia = new ImpactAssessment
            {
                Number = await numbers.NextAsync(ImpactType, e.SiteId, ct), EquipmentId = e.Id, CalibrationEventId = ev.Id, SiteId = e.SiteId, DepartmentId = e.DepartmentId,
                LastKnownGoodOn = lastGood, AffectedUntil = r.PerformedOn, Status = ImpactStatus.Open
            };
            db.ImpactAssessments.Add(ia);
        }
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(EquipmentType, e.Id, $"{e.Manufacturer} {e.Model} {e.SerialNumber} {e.Location}", ct);
        await m.IndexAsync(EventType, ev.Id, ev.CertificateNumber, ct);
        if (ia is not null)
        {
            await m.IndexAsync(ImpactType, ia.Id, $"Out of tolerance {e.Number}", ct);
            var recipients = (await access.UsersWithPermissionAsync(Permissions.CalibrationApprove, e.SiteId, e.DepartmentId, ct)).Append(e.OwnerId).Distinct();
            await notifier.NotifyAsync(recipients, $"{e.Number} out of tolerance: quarantined", $"Impact assessment {ia.Number} is open for product measured since {(lastGood?.ToString("yyyy-MM-dd") ?? "first use")}.", await m.RecordAsync(ImpactType, ia.Id, ct), ct: ct);
        }
        await tx.CommitAsync(ct);
        return (ev, ia);
    }

    public async Task CompleteImpactAsync(Guid impactId, CompleteImpactRequest r, CancellationToken ct)
    {
        await m.ViewAsync(ImpactType, impactId, ct);
        var ia = await db.ImpactAssessments.FirstAsync(x => x.Id == impactId, ct);
        await m.RequireAsync(Permissions.CalibrationApprove, ia.SiteId, ia.DepartmentId, ct);
        if (ia.Status == ImpactStatus.Completed) throw new ConflictException("The impact assessment is already complete.");
        ModuleSupport.Required(r.AffectedProduct, "Affected product", 4000);
        ModuleSupport.Required(r.Assessment, "Assessment", 10000);
        ModuleSupport.Required(r.Decision, "Decision", 4000);
        ia.AffectedProduct = r.AffectedProduct.Trim();
        ia.Assessment = r.Assessment.Trim();
        ia.Decision = r.Decision.Trim();
        ia.Status = ImpactStatus.Completed;
        ia.CompletedById = m.UserId;
        ia.CompletedAt = m.Now;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(ImpactType, ia.Id, ia.Assessment, ct);
    }

    /// <summary>Raises an NCR for product affected by an out-of-tolerance result, linked to the impact assessment.</summary>
    public async Task<Domain.Quality.Ncr> RaiseNcrAsync(Guid impactId, string title, string description, CancellationToken ct)
    {
        await m.ViewAsync(ImpactType, impactId, ct);
        var ia = await db.ImpactAssessments.AsNoTracking().FirstAsync(x => x.Id == impactId, ct);
        var e = await db.Equipment.AsNoTracking().FirstAsync(x => x.Id == ia.EquipmentId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var n = await ncrs.CreateAsync(new CreateNcrRequest(title, description, "Calibration", ia.SiteId, ia.DepartmentId, null, null, null, null, m.Today, Severity.Major), ct);
        db.RecordLinks.Add(new Domain.Shared.RecordLink { FromType = ImpactType, FromId = ia.Id, ToType = NcrService.RecordType, ToId = n.Id, LinkType = $"Out of tolerance {e.Number}" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return n;
    }

    /// <summary>Return to service needs every impact assessment completed and a passing calibration after the failure.</summary>
    public async Task ReturnToServiceAsync(Guid equipmentId, CancellationToken ct)
    {
        await m.ViewAsync(EquipmentType, equipmentId, ct);
        var e = await db.Equipment.FirstAsync(x => x.Id == equipmentId, ct);
        await m.RequireAsync(Permissions.CalibrationApprove, e.SiteId, e.DepartmentId, ct);
        if (e.Status is not (EquipmentStatus.Quarantined or EquipmentStatus.OutOfService)) throw new ConflictException("The equipment is already in service.");
        if (await db.ImpactAssessments.AnyAsync(x => x.EquipmentId == e.Id && x.Status == ImpactStatus.Open, ct)) throw new AppException("Complete the open impact assessment first.");
        var latest = await db.CalibrationEvents.Where(x => x.EquipmentId == e.Id).OrderByDescending(x => x.PerformedOn).ThenByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (latest is null || latest.Result is not (CalibrationResult.Pass or CalibrationResult.PassAfterAdjustment)) throw new AppException("Record a passing calibration before returning to service.");
        e.Status = EquipmentStatus.Active;
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(EquipmentType, e.Id, null, ct);
    }
}
