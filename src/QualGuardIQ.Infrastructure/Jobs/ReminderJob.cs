using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Jobs;

/// <summary>
/// Daily due-date reminders across modules: actions, training, calibration, supplier certificates, supplier reviews,
/// SCAR responses and document periodic reviews. Each reminder is sent once (tracked in ReminderLog);
/// overdue items get a second, separate reminder.
/// </summary>
public sealed class ReminderJob(AppDbContext db, INotifier notifier, IRecordRegistry registry, ISiteCalendar calendar, ILogger<ReminderJob> log)
{
    public const int LeadDays = 14;
    public const int CertificateLeadDays = 30;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var today = await calendar.TodayAsync(null, ct);
        var soon = today.AddDays(LeadDays);
        var items = new List<(string Key, Guid User, string Title, string Type, Guid Id)>();
        string Phase(DateOnly due) => due < today ? "overdue" : "due";

        foreach (var a in await db.ActionItems.Where(a => (a.Status == ActionStatus.Open || a.Status == ActionStatus.InProgress) && a.DueDate <= soon).Select(a => new { a.Id, a.Number, a.Title, a.AssigneeId, a.DueDate }).ToListAsync(ct))
            items.Add(($"action:{a.Id}:{Phase(a.DueDate)}", a.AssigneeId, $"Action {a.Number} {(a.DueDate < today ? "is overdue" : $"is due {a.DueDate:yyyy-MM-dd}")}: {a.Title}", "Action", a.Id));

        foreach (var t in await db.TrainingAssignments.Where(t => t.Status == AssignmentStatus.Assigned && t.DueOn <= soon)
                     .Join(db.TrainingCourses, t => t.CourseId, c => c.Id, (t, c) => new { t.Id, t.UserId, t.DueOn, c.Number, c.Title, CourseId = c.Id }).ToListAsync(ct))
            items.Add(($"training:{t.Id}:{Phase(t.DueOn)}", t.UserId, $"Training {t.Number} {(t.DueOn < today ? "is overdue" : $"is due {t.DueOn:yyyy-MM-dd}")}: {t.Title}", "TrainingCourse", t.CourseId));

        foreach (var e in await db.Equipment.Where(e => e.Status == EquipmentStatus.Active && e.NextDueOn != null && e.NextDueOn <= soon).Select(e => new { e.Id, e.Number, e.Name, e.OwnerId, Due = e.NextDueOn!.Value }).ToListAsync(ct))
            items.Add(($"calibration:{e.Id}:{e.Due:yyyyMMdd}:{Phase(e.Due)}", e.OwnerId, $"Calibration of {e.Number} {e.Name} {(e.Due < today ? "is overdue" : $"is due {e.Due:yyyy-MM-dd}")}", "Equipment", e.Id));

        var certLimit = today.AddDays(CertificateLeadDays);
        foreach (var c in await db.SupplierCertifications.Where(c => c.ExpiresOn <= certLimit)
                     .Join(db.Suppliers.Where(s => s.Status != SupplierStatus.Disqualified), c => c.SupplierId, s => s.Id, (c, s) => new { c.Id, c.Standard, c.ExpiresOn, s.Code, s.OwnerId, SupplierId = s.Id }).ToListAsync(ct))
            items.Add(($"cert:{c.Id}:{c.ExpiresOn:yyyyMMdd}:{(c.ExpiresOn < today ? "expired" : "expiring")}", c.OwnerId,
                $"Supplier {c.Code} certificate {c.Standard} {(c.ExpiresOn < today ? "has expired" : $"expires {c.ExpiresOn:yyyy-MM-dd}")}", "Supplier", c.SupplierId));

        foreach (var s in await db.Suppliers.Where(s => s.NextReviewOn != null && s.NextReviewOn <= soon && s.Status != SupplierStatus.Disqualified && s.Status != SupplierStatus.Prospective)
                     .Select(s => new { s.Id, s.Code, s.LegalName, s.OwnerId, Due = s.NextReviewOn!.Value }).ToListAsync(ct))
            items.Add(($"supplier-review:{s.Id}:{s.Due:yyyyMMdd}:{Phase(s.Due)}", s.OwnerId, $"Periodic review of supplier {s.Code} {s.LegalName} {(s.Due < today ? "is overdue" : $"is due {s.Due:yyyy-MM-dd}")}", "Supplier", s.Id));

        foreach (var c in await db.SupplierCorrectiveActions.Where(c => c.Status == ScarStatus.Issued && c.ResponseDueOn < today).Select(c => new { c.Id, c.Number, c.OwnerId, c.ResponseDueOn }).ToListAsync(ct))
            items.Add(($"scar:{c.Id}:{c.ResponseDueOn:yyyyMMdd}:overdue", c.OwnerId, $"Supplier response to {c.Number} is overdue (due {c.ResponseDueOn:yyyy-MM-dd})", SupplierQualityTypes.Scar, c.Id));

        foreach (var d in await db.Documents.Where(d => d.NextReviewDue != null && d.NextReviewDue <= soon).Select(d => new { d.Id, d.Number, d.Title, d.OwnerId, Due = d.NextReviewDue!.Value }).ToListAsync(ct))
            items.Add(($"doc-review:{d.Id}:{d.Due:yyyyMMdd}:{Phase(d.Due)}", d.OwnerId, $"Periodic review of {d.Number} {d.Title} {(d.Due < today ? "is overdue" : $"is due {d.Due:yyyy-MM-dd}")}", "Document", d.Id));

        if (items.Count == 0) return 0;
        var keys = items.Select(i => i.Key).ToList();
        var sent = (await db.ReminderLogs.Where(r => keys.Contains(r.Key)).Select(r => r.Key).ToListAsync(ct)).ToHashSet();
        var count = 0;
        foreach (var i in items.Where(i => !sent.Contains(i.Key)))
        {
            RecordInfo? info = null;
            try { info = await registry.GetAsync(i.Type, i.Id, ct); } catch (Exception e) when (e is not OperationCanceledException) { log.LogDebug(e, "No record info for {Type} {Id}", i.Type, i.Id); }
            await notifier.NotifyAsync([i.User], i.Title, null, info, email: true, ct);
            db.ReminderLogs.Add(new ReminderLog { Key = i.Key, SentAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync(ct);
            count++;
        }
        if (count > 0) log.LogInformation("Sent {Count} due-date reminders", count);
        return count;
    }
}

internal static class SupplierQualityTypes { public const string Scar = "SupplierCorrectiveAction"; }
