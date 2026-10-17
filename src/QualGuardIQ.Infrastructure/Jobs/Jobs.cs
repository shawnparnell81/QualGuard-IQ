using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Jobs;

/// <summary>Identity used for audit entries written by background jobs (or the run-as identity of a scheduled run's scope).</summary>
public sealed class SystemCurrentUser(QualGuardIQ.Infrastructure.Services.RunAsContext runAs) : ICurrentUser
{
    public Guid? UserId => runAs.UserId;
    public string UserName => runAs.UserName ?? "System";
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
                using var msg = new MailMessage(from, to, $"[{appName}] {n.Title}", body);
                if (n.AttachmentId is not null)
                {
                    // report files are only mailed over TLS and only while the retention period lasts; otherwise the recipient uses the link
                    var file = smtp.EnableSsl ? await QualGuardIQ.Infrastructure.Modules.Platform.ReportDistributionService.EmailAttachmentAsync(db, n.AttachmentId, DateTimeOffset.UtcNow, ct) : null;
                    if (file?.Data is { } data) msg.Attachments.Add(new Attachment(new MemoryStream(data), file.FileName, file.ContentType));
                    else msg.Body += "\n\nThe attached file was not included in this email (TLS is off or the file expired). Open the link above to view the report.";
                }
                await smtp.SendMailAsync(msg, ct);
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

/// <summary>Every minute: move audit/approval activity onto the platform event bus and deliver due webhooks.</summary>
public sealed class PlatformEventJob(QualGuardIQ.Infrastructure.Modules.Platform.PlatformService platform, ILogger<PlatformEventJob> log)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct)
    {
        var n = await platform.PumpAsync(ct);
        var d = await platform.DeliverAsync(ct);
        if (n + d > 0) log.LogInformation("Platform events: {Events} published, {Deliveries} webhook deliveries attempted", n, d);
    }
}

/// <summary>Every minute: queue SMS/push messages for new notifications and send due messages within each provider's rate limit.</summary>
public sealed class MessagingDeliveryJob(QualGuardIQ.Infrastructure.Modules.Messaging.MessagingService messaging)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 300)]
    public Task RunAsync(CancellationToken ct) => messaging.RunAsync(ct);
}

/// <summary>Daily: purge stored generation prompts/responses and old request metadata per the retention settings.</summary>
public sealed class AiRetentionJob(QualGuardIQ.Infrastructure.Modules.Intelligence.AiBoundaryService ai, ILogger<AiRetentionJob> log)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct)
    {
        var (purged, deleted) = await ai.PurgeAsync(ct);
        if (purged + deleted > 0) log.LogInformation("Generation logs: {Purged} content purged, {Deleted} deleted", purged, deleted);
    }
}

/// <summary>Every 5 minutes: send due scheduled reports.</summary>
public sealed class ReportScheduleJob(QualGuardIQ.Infrastructure.Modules.Platform.ReportDistributionService distribution)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 1200)]
    public Task RunAsync(CancellationToken ct) => distribution.RunDueAsync(ct);
}

/// <summary>Every 15 minutes: escalate approval stages waiting beyond their designed escalation time.</summary>
public sealed class ApprovalEscalationJob(QualGuardIQ.Infrastructure.Services.WorkflowService workflow, QualGuardIQ.Infrastructure.Modules.Platform.RuleEngine rules)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken ct) { await workflow.EscalateAsync(ct); await rules.EscalateAsync(ct); }
}

/// <summary>Every minute: sends pending label prints and retries failed ones whose back-off has passed.</summary>
public sealed class LabelPrintQueueJob(QualGuardIQ.Infrastructure.Modules.Logistics.LabelPrintService labels)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 120)]
    public Task RunAsync(CancellationToken ct) => labels.ProcessDueAsync(ct);
}

/// <summary>Every 30 minutes: refreshes carrier tracking for shipped shipments with active carrier labels and marks delivered shipments.</summary>
public sealed class CarrierTrackingJob(QualGuardIQ.Infrastructure.Modules.Logistics.CarrierIntegrationService carriers)
{
    [Hangfire.DisableConcurrentExecution(timeoutInSeconds: 600)]
    public Task RunAsync(CancellationToken ct) => carriers.RefreshTrackingAsync(ct);
}
