using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Quality;

public sealed record MaintenancePlanRequest(string Title, string? Instructions, int IntervalDays, DateOnly? FirstDueOn);
public sealed record RecordMaintenanceRequest(MaintenanceKind Kind, Guid? PlanId, DateOnly PerformedOn, string PerformedBy, string Description, decimal? DowntimeHours,
    bool ReturnToService = false, bool CalibrationRequired = false, DateOnly? QualificationDueOn = null);

/// <summary>
/// Preventive maintenance schedules and the maintenance/repair/breakdown/qualification history of equipment and tooling.
/// A breakdown takes equipment down; a repair returns it to service, or to quarantine when the repair affects measurement
/// (a calibration on or after the repair date is then required by the calibration return-to-service rule).
/// </summary>
public sealed class MaintenanceService(AppDbContext db, ModuleSupport m, INumberingService numbers)
{
    public const string EventType = "MaintenanceEvent";

    private async Task<Equipment> LoadAsync(Guid equipmentId, CancellationToken ct)
    {
        await m.ViewAsync(CalibrationService.EquipmentType, equipmentId, ct);
        var e = await db.Equipment.FirstAsync(x => x.Id == equipmentId, ct);
        await m.RequireAsync(Permissions.MaintenanceEdit, e.SiteId, e.DepartmentId, ct);
        if (e.Status == EquipmentStatus.Retired) throw new ConflictException("Retired equipment cannot be maintained.");
        return e;
    }

    public async Task<MaintenancePlan> AddPlanAsync(Guid equipmentId, MaintenancePlanRequest r, CancellationToken ct)
    {
        var e = await LoadAsync(equipmentId, ct);
        ModuleSupport.Required(r.Title, "Task", 200);
        if (r.IntervalDays is < 1 or > 3650) throw new AppException("The interval must be between 1 and 3650 days.");
        if (r.Instructions?.Length > 4000) throw new AppException("Instructions are limited to 4000 characters.");
        var p = new MaintenancePlan { EquipmentId = e.Id, Title = r.Title.Trim(), Instructions = r.Instructions?.Trim(), IntervalDays = r.IntervalDays,
            NextDueOn = r.FirstDueOn ?? (await m.TodayAsync(e.SiteId, ct)).AddDays(r.IntervalDays) };
        db.MaintenancePlans.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task DeactivatePlanAsync(Guid equipmentId, Guid planId, CancellationToken ct)
    {
        await LoadAsync(equipmentId, ct);
        var p = await db.MaintenancePlans.FirstOrDefaultAsync(x => x.Id == planId && x.EquipmentId == equipmentId, ct) ?? throw new NotFoundException("Maintenance plan");
        p.IsActive = false;
        await db.SaveChangesAsync(ct);
    }

    public async Task<MaintenanceEvent> RecordAsync(Guid equipmentId, RecordMaintenanceRequest r, CancellationToken ct)
    {
        var e = await LoadAsync(equipmentId, ct);
        ModuleSupport.Required(r.PerformedBy, "Performed by", 200);
        ModuleSupport.Required(r.Description, "Description", 4000);
        if (r.PerformedOn > await m.TodayAsync(e.SiteId, ct)) throw new AppException("The date cannot be in the future.");
        if (r.DowntimeHours is < 0) throw new AppException("Downtime cannot be negative.");
        MaintenancePlan? plan = null;
        if (r.Kind == MaintenanceKind.Preventive)
        {
            if (r.PlanId is null) throw new AppException("Choose the maintenance plan this work completes.");
            plan = await db.MaintenancePlans.FirstOrDefaultAsync(x => x.Id == r.PlanId && x.EquipmentId == e.Id && x.IsActive, ct) ?? throw new AppException("The maintenance plan does not exist or is inactive.");
        }
        else if (r.PlanId is not null) throw new AppException("Only preventive maintenance completes a plan.");
        if (r.ReturnToService && r.Kind != MaintenanceKind.Repair) throw new AppException("Only a repair returns equipment to service.");
        if (r.ReturnToService && e.Status != EquipmentStatus.Down) throw new ConflictException("The equipment is not down.");
        if (r.CalibrationRequired && !e.RequiresCalibration) throw new AppException("This equipment is not under calibration control.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ev = new MaintenanceEvent { Number = await numbers.NextAsync(EventType, e.SiteId, ct), EquipmentId = e.Id, SiteId = e.SiteId, DepartmentId = e.DepartmentId, Kind = r.Kind, PlanId = plan?.Id,
            PerformedOn = r.PerformedOn, PerformedBy = r.PerformedBy.Trim(), Description = r.Description.Trim(), DowntimeHours = r.DowntimeHours, ReturnedToService = r.ReturnToService, CalibrationRequired = r.CalibrationRequired };
        db.MaintenanceEvents.Add(ev);
        switch (r.Kind)
        {
            case MaintenanceKind.Preventive:
                plan!.LastDoneOn = r.PerformedOn; plan.NextDueOn = r.PerformedOn.AddDays(plan.IntervalDays);
                break;
            case MaintenanceKind.Breakdown:
                if (e.Status == EquipmentStatus.Active) e.Status = EquipmentStatus.Down;
                break;
            case MaintenanceKind.Repair when r.ReturnToService:
                if (r.CalibrationRequired) { e.Status = EquipmentStatus.Quarantined; e.RecalibrateAfter = r.PerformedOn; }
                else e.Status = EquipmentStatus.Active;
                break;
            case MaintenanceKind.Qualification:
                e.QualifiedOn = r.PerformedOn; e.QualificationDueOn = r.QualificationDueOn; e.QualificationNotes = r.Description.Trim();
                break;
        }
        if (r.CalibrationRequired && !r.ReturnToService && e.RequiresCalibration) { if (e.Status == EquipmentStatus.Active) e.Status = EquipmentStatus.Quarantined; e.RecalibrateAfter = r.PerformedOn; }
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(EventType, ev.Id, ev.Description, ct);
        await tx.CommitAsync(ct);
        return ev;
    }
}
