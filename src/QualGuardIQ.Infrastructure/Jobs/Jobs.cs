using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Jobs;

/// <summary>Identity used for audit entries written by background jobs.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string UserName => "System";
    public string? IpAddress => null;
    public string? CorrelationId => null;
}

/// <summary>Sends email copies of notifications through SMTP when Email:SmtpHost is configured.</summary>
public sealed class NotificationEmailJob(AppDbContext db, IConfiguration config, ILogger<NotificationEmailJob> log)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var host = config["Email:SmtpHost"];
        if (string.IsNullOrWhiteSpace(host)) return;
        var from = config["Email:From"] ?? throw new InvalidOperationException("Email:From is required when SMTP is configured.");
        var baseUrl = (config["App:BaseUrl"] ?? "").TrimEnd('/');
        var appName = await db.OrganizationSettings.Select(s => s.ApplicationName).FirstAsync(ct);
        var batch = await db.Notifications.Where(n => n.EmailRequested && n.EmailSentAt == null && n.EmailAttempts < 5).OrderBy(n => n.Id).Take(100).ToListAsync(ct);
        if (batch.Count == 0) return;
        var ids = batch.Select(n => n.UserId).Distinct().ToList();
        var emails = await db.Users.Where(u => ids.Contains(u.Id) && u.IsActive).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        using var smtp = new SmtpClient(host, config.GetValue("Email:SmtpPort", 587)) { EnableSsl = config.GetValue("Email:UseTls", true) };
        if (!string.IsNullOrWhiteSpace(config["Email:Username"])) smtp.Credentials = new System.Net.NetworkCredential(config["Email:Username"], config["Email:Password"]);
        foreach (var n in batch)
        {
            n.EmailAttempts++;
            if (!emails.TryGetValue(n.UserId, out var to) || string.IsNullOrWhiteSpace(to)) { n.EmailRequested = false; continue; }
            try
            {
                var body = $"{n.Body}\n\n{(n.Link is null ? "" : baseUrl + n.Link)}".Trim();
                await smtp.SendMailAsync(new MailMessage(from, to, $"[{appName}] {n.Title}", body), ct);
                n.EmailSentAt = DateTimeOffset.UtcNow;
            }
            catch (SmtpException e) { log.LogWarning(e, "Email for notification {Id} failed", n.Id); }
        }
        await db.SaveChangesAsync(ct);
    }
}

public sealed class DocumentReleaseJob(DocumentService docs, ILogger<DocumentReleaseJob> log)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var n = await docs.ReleaseDueAsync(ct);
        if (n > 0) log.LogInformation("Released {Count} document revisions that reached their effective date", n);
    }
}

/// <summary>Runs due ERP syncs (every minute; each mapping has its own cron). One run at a time across workers.</summary>
public sealed class ErpSyncJob(QualGuardIQ.Infrastructure.Modules.Integration.ErpSyncService sync)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 600)]
    public Task RunAsync(CancellationToken ct) => sync.RunDueAsync(ct);
}

/// <summary>Evaluates active alert rules every 15 minutes.</summary>
public sealed class AlertJob(QualGuardIQ.Infrastructure.Modules.Analytics.AlertService alerts)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 600)]
    public Task RunAsync(CancellationToken ct) => alerts.EvaluateAsync(ct);
}

/// <summary>Writes due incremental dataset exports (NDJSON) to object storage.</summary>
public sealed class DataExportJob(QualGuardIQ.Infrastructure.Modules.Analytics.DatasetService data)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public Task RunAsync(CancellationToken ct) => data.RunExportsAsync(ct);
}

/// <summary>Nightly: auto-assign curricula and assign refresher training before qualifications lapse.</summary>
public sealed class WorkforceJob(QualGuardIQ.Infrastructure.Modules.Enterprise.WorkforceService workforce, ILogger<WorkforceJob> log)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var (a, r) = await workforce.AutoAssignAsync(ct);
        if (a + r > 0) log.LogInformation("Assigned {Assigned} curriculum courses and {Refreshers} refresher trainings", a, r);
    }
}
