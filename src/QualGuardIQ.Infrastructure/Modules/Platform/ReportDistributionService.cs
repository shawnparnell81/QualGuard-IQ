using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Application.Security;
using QualGuardIQ.Domain.Platform;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Engineering;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Modules.Reports;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Platform;

public sealed record DistributionInput(string Name, ReportScheduleSource Source, Guid? SavedReportId, string? BoardReportKind, string Cron, string TimeZone, List<Guid>? Recipients, List<Guid>? RecipientRoleIds,
    ReportDeliveryMode DeliveryMode, bool GenerateDraftEdition = false, int MaxAttempts = 3);
public sealed record PauseInput(string? Reason);
public sealed record DistributionScheduleRow(Guid Id, string? Name, ReportScheduleSource Source, Guid? SavedReportId, string? Report, string? BoardReportKind, string Cron, string TimeZone, List<Guid> Recipients, List<Guid> RecipientRoleIds,
    ReportDeliveryMode DeliveryMode, bool GenerateDraftEdition, int MaxAttempts, bool Paused, DateTimeOffset? PausedAt, string? PauseReason, DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, string? LastStatus,
    int FailedAttempts, DateTimeOffset? RetryAt, Guid OwnerId, string? Owner);
public sealed record DistributionRow(Guid Id, DistributionTrigger Trigger, int Attempt, DistributionStatus Status, ReportDeliveryMode DeliveryMode, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, int Recipients, int Delivered,
    int Excluded, string? Error, string? Note, Guid? EditionId, int? EditionVersion, Guid? ArtifactId, DateTimeOffset? NextAttemptAt, List<RecipientRow> RecipientDetail);
public sealed record RecipientRow(Guid UserId, string? Name, DistributionOutcome Outcome, string? Reason, DateTimeOffset? DownloadedAt);
public sealed record PolicyInput(bool AttachmentsEnabled, bool AllowPdf, bool AllowXlsx, bool AllowCsv, bool AllowConfidential, long MaxAttachmentBytes, int RetentionDays, List<string>? AllowedEmailDomains);
public sealed record EditionRow(Guid Id, string Kind, int Version, string Title, EditionStatus Status, DateTimeOffset GeneratedAt, string? GeneratedBy, bool ScopeAllSites, int ScopeSites, DateTimeOffset? ApprovedAt, string? ApprovedBy,
    string? Notes, bool Confidential);
public sealed record EditionDecision(string Notes);
public sealed record GroupRow(Guid Id, string Name, int Members);

/// <summary>
/// Scheduled distribution of saved reports and board reports: time zone aware cron, people and role groups, permission checks on every
/// run, secure link by default with optional approved attachments, retry with back-off, failure notification, pause/resume, manual runs
/// and a full distribution history. A run executes with its owner's permissions in its own scope.
/// </summary>
public sealed class ReportDistributionService(AppDbContext db, ModuleSupport m, IAccessService access, INotifier notifier, IClock clock, IServiceScopeFactory scopes, IServiceProvider sp, IConfiguration config,
    ILogger<ReportDistributionService> log)
{
    public static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60), TimeSpan.FromHours(4)];
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---------- policy ----------
    public async Task<ReportDeliveryPolicy> PolicyAsync(CancellationToken ct) =>
        await db.ReportDeliveryPolicies.AsNoTracking().OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct) ?? new ReportDeliveryPolicy();

    public async Task<ReportDeliveryPolicy> SavePolicyAsync(PolicyInput r, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.PlatformAdmin, ct)) throw new ForbiddenException("Changing the report delivery policy needs platform.admin.");
        if (r.MaxAttachmentBytes is < 10_000 or > 25 * 1024 * 1024) throw new AppException("The attachment size limit must be between 10 KB and 25 MB (most mail servers refuse larger messages).");
        if (r.RetentionDays is < 1 or > 365) throw new AppException("Retention must be 1–365 days.");
        var domains = (r.AllowedEmailDomains ?? []).Select(d => d.Trim().TrimStart('@').ToLowerInvariant()).Where(d => d.Length > 0).Distinct().ToList();
        if (domains.Any(d => d.Length > 200 || !d.Contains('.') || d.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-')))) throw new AppException("Enter email domains such as example.com.");
        var p = await db.ReportDeliveryPolicies.OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (p is null) { p = new ReportDeliveryPolicy(); db.ReportDeliveryPolicies.Add(p); }
        p.AttachmentsEnabled = r.AttachmentsEnabled; p.AllowPdf = r.AllowPdf; p.AllowXlsx = r.AllowXlsx; p.AllowCsv = r.AllowCsv; p.AllowConfidential = r.AllowConfidential;
        p.MaxAttachmentBytes = r.MaxAttachmentBytes; p.RetentionDays = r.RetentionDays; p.AllowedEmailDomains = domains;
        await db.SaveChangesAsync(ct);
        return p;
    }

    static bool ModeAllowed(ReportDeliveryPolicy p, ReportDeliveryMode mode) => mode switch
    {
        ReportDeliveryMode.Link => true,
        ReportDeliveryMode.PdfAttachment => p.AttachmentsEnabled && p.AllowPdf,
        ReportDeliveryMode.XlsxAttachment => p.AttachmentsEnabled && p.AllowXlsx,
        _ => p.AttachmentsEnabled && p.AllowCsv,
    };

    /// <summary>Roles that can be chosen as recipient groups (names only; membership is resolved and permission-checked on each run).</summary>
    public async Task<List<GroupRow>> GroupsAsync(CancellationToken ct) =>
        await db.QmsRoles.AsNoTracking().OrderBy(r => r.Name).Select(r => new GroupRow(r.Id, r.Name, db.UserRoleAssignments.Where(a => a.RoleId == r.Id).Select(a => a.UserId).Distinct().Count())).ToListAsync(ct);

    // ---------- schedules ----------
    public static DateTimeOffset? NextInZone(string cron, string zoneId, DateTimeOffset fromUtc)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var c = CronSchedule.Parse(cron);
        // evaluate the expression on wall-clock time in the zone: shift "now" into local time, find the next local match, convert back
        var local = TimeZoneInfo.ConvertTime(fromUtc, tz).DateTime;
        var cursor = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.Zero);
        for (var i = 0; i < 4; i++)
        {
            var n = c.Next(cursor);
            if (n is null) return null;
            var wall = DateTime.SpecifyKind(n.Value.DateTime, DateTimeKind.Unspecified);
            if (tz.IsInvalidTime(wall)) { cursor = n.Value; continue; } // skipped by a daylight-saving jump
            var offset = tz.IsAmbiguousTime(wall) ? tz.GetAmbiguousTimeOffsets(wall).Max() : tz.GetUtcOffset(wall);
            var utc = new DateTimeOffset(wall, offset).ToUniversalTime();
            if (utc > fromUtc) return utc;
            cursor = n.Value;
        }
        return null;
    }

    async Task<ReportSchedule> OwnAsync(Guid id, CancellationToken ct)
    {
        var s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Schedule");
        if (s.OwnerId != m.UserId && !await m.CanAnyAsync(Permissions.PlatformAdmin, ct)) throw new NotFoundException("Schedule");
        return s;
    }

    public async Task<List<DistributionScheduleRow>> SchedulesAsync(bool all, CancellationToken ct)
    {
        var uid = m.UserId;
        var q = db.ReportSchedules.AsNoTracking();
        if (!(all && await m.CanAnyAsync(Permissions.PlatformAdmin, ct))) q = q.Where(x => x.OwnerId == uid);
        var list = await q.OrderBy(x => x.Name).ThenBy(x => x.CreatedAt).ToListAsync(ct);
        var reps = list.Select(x => x.SavedReportId).OfType<Guid>().Distinct().ToList();
        var names = await db.SavedReports.AsNoTracking().Where(r => reps.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var owners = list.Select(x => x.OwnerId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return list.Select(s => Row(s, s.SavedReportId is { } r ? names.GetValueOrDefault(r) : null, users.GetValueOrDefault(s.OwnerId))).ToList();
    }

    static DistributionScheduleRow Row(ReportSchedule s, string? report, string? owner) => new(s.Id, s.Name, s.Source, s.SavedReportId, report, s.BoardReportKind, s.Cron, s.TimeZone, s.Recipients, s.RecipientRoleIds, s.DeliveryMode,
        s.GenerateDraftEdition, s.MaxAttempts, s.PausedAt is not null || !s.Enabled, s.PausedAt, s.PauseReason, s.NextRunAt, s.LastRunAt, s.LastStatus, s.FailedAttempts, s.RetryAt, s.OwnerId, owner);

    public async Task<DistributionScheduleRow> ScheduleAsync(Guid id, CancellationToken ct)
    {
        var s = await OwnAsync(id, ct);
        var rep = s.SavedReportId is { } r ? await db.SavedReports.AsNoTracking().Where(x => x.Id == r).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        return Row(s, rep, await db.Users.AsNoTracking().Where(u => u.Id == s.OwnerId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct));
    }

    async Task ValidateAsync(DistributionInput r, ReportSchedule? existing, CancellationToken ct)
    {
        ModuleSupport.Required(r.Name, "Name", 200);
        try { CronSchedule.Parse(r.Cron.Trim()); } catch (Exception e) when (e is FormatException or OverflowException or IndexOutOfRangeException) { throw new AppException("Enter a valid 5-field cron expression, e.g. '0 7 * * 1'."); }
        try { TimeZoneInfo.FindSystemTimeZoneById(r.TimeZone); } catch (TimeZoneNotFoundException) { throw new AppException($"Unknown time zone '{r.TimeZone}'. Use an IANA name such as America/New_York."); }
        if (r.MaxAttempts is < 1 or > 5) throw new AppException("Attempts must be 1–5.");
        var people = (r.Recipients ?? []).Distinct().ToList(); var roles = (r.RecipientRoleIds ?? []).Distinct().ToList();
        if (people.Count + roles.Count == 0) throw new AppException("Add at least one recipient or recipient group.");
        if (people.Count > 0)
        {
            var found = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).Select(u => new { u.Id, u.IsActive, u.PortalKind }).ToListAsync(ct);
            if (found.Count != people.Count || found.Any(u => !u.IsActive)) throw new AppException("Every recipient must be an active user.");
            if (found.Any(u => u.PortalKind is not null)) throw new AppException("Customer and supplier portal accounts cannot receive scheduled internal reports.");
        }
        if (roles.Count > 0 && await db.QmsRoles.CountAsync(x => roles.Contains(x.Id), ct) != roles.Count) throw new AppException("Unknown recipient group (role).");
        var confidential = false;
        switch (r.Source)
        {
            case ReportScheduleSource.SavedReport:
            {
                var rep = await db.SavedReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.SavedReportId && (x.OwnerId == m.UserId || x.Shared), ct) ?? throw new AppException("Choose a saved report you can open.");
                if (!rep.Shared && (roles.Count > 0 || people.Any(x => x != m.UserId))) throw new AppException("Share the report before scheduling it to other people.");
                confidential = rep.Confidential;
                break;
            }
            case ReportScheduleSource.BoardReport:
                if (r.BoardReportKind is null || !IntelligenceService.BoardReports.Contains(r.BoardReportKind)) throw new AppException($"Board report must be one of: {string.Join(", ", IntelligenceService.BoardReports)}.");
                if (!await m.CanAnyAsync(Permissions.EnterpriseView, ct)) throw new ForbiddenException("Scheduling board reports needs enterprise.view.");
                confidential = true;
                break;
            default: throw new AppException("Unknown report source.");
        }
        if (r.DeliveryMode != ReportDeliveryMode.Link)
        {
            var p = await PolicyAsync(ct);
            if (!p.AttachmentsEnabled) throw new AppException("Attached delivery is switched off. An administrator must enable it in the report delivery policy; the secure link is the default.");
            if (!ModeAllowed(p, r.DeliveryMode)) throw new AppException($"The delivery policy does not allow {r.DeliveryMode} attachments.");
            if (!await m.CanAnyAsync(Permissions.ReportsExport, ct)) throw new ForbiddenException("Attaching report files needs the reports.export permission.");
            if (confidential && !p.AllowConfidential) throw new AppException("This report is confidential. Confidential reports are delivered as a secure link; the delivery policy does not allow attaching them.");
        }
        if (existing is not null && existing.OwnerId != m.UserId) throw new ForbiddenException("Only the owner can change a schedule.");
    }

    void Apply(ReportSchedule s, DistributionInput r)
    {
        s.Name = r.Name.Trim(); s.Source = r.Source; s.SavedReportId = r.Source == ReportScheduleSource.SavedReport ? r.SavedReportId : null; s.BoardReportKind = r.Source == ReportScheduleSource.BoardReport ? r.BoardReportKind : null;
        s.Cron = r.Cron.Trim(); s.TimeZone = r.TimeZone; s.Recipients = (r.Recipients ?? []).Distinct().ToList(); s.RecipientRoleIds = (r.RecipientRoleIds ?? []).Distinct().ToList();
        s.DeliveryMode = r.DeliveryMode; s.GenerateDraftEdition = r.Source == ReportScheduleSource.BoardReport && r.GenerateDraftEdition; s.MaxAttempts = r.MaxAttempts;
        s.Format = r.DeliveryMode switch { ReportDeliveryMode.XlsxAttachment => ReportFormat.Xlsx, ReportDeliveryMode.CsvAttachment => ReportFormat.Csv, ReportDeliveryMode.PdfAttachment => ReportFormat.Pdf, _ => s.Format };
        s.NextRunAt = NextInZone(s.Cron, s.TimeZone, clock.UtcNow); s.FailedAttempts = 0; s.RetryAt = null;
    }

    public async Task<Guid> CreateAsync(DistributionInput r, CancellationToken ct)
    {
        await ValidateAsync(r, null, ct);
        var s = new ReportSchedule { OwnerId = m.UserId, Enabled = true };
        Apply(s, r);
        db.ReportSchedules.Add(s);
        await db.SaveChangesAsync(ct);
        return s.Id;
    }

    public async Task UpdateAsync(Guid id, DistributionInput r, CancellationToken ct)
    {
        var s = await OwnAsync(id, ct);
        await ValidateAsync(r, s, ct);
        Apply(s, r);
        await db.SaveChangesAsync(ct);
    }

    public async Task PauseAsync(Guid id, PauseInput r, CancellationToken ct)
    {
        var s = await OwnAsync(id, ct);
        if (s.PausedAt is not null) throw new ConflictException("The schedule is already paused.");
        if (r.Reason?.Length > 500) throw new AppException("The reason is longer than 500 characters.");
        s.PausedAt = clock.UtcNow; s.PausedById = m.UserId; s.PauseReason = string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason.Trim(); s.RetryAt = null; s.FailedAttempts = 0;
        await db.SaveChangesAsync(ct);
    }

    public async Task ResumeAsync(Guid id, CancellationToken ct)
    {
        var s = await OwnAsync(id, ct);
        if (s.PausedAt is null && s.Enabled) throw new ConflictException("The schedule is not paused.");
        s.PausedAt = null; s.PausedById = null; s.PauseReason = null; s.Enabled = true;
        s.NextRunAt = NextInZone(s.Cron, s.TimeZone, clock.UtcNow); // missed runs while paused are not sent afterwards
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var s = await OwnAsync(id, ct);
        db.ReportSchedules.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DistributionRow>> HistoryAsync(Guid scheduleId, CancellationToken ct)
    {
        await OwnAsync(scheduleId, ct);
        var runs = await db.ReportDistributions.AsNoTracking().Where(x => x.ScheduleId == scheduleId).OrderByDescending(x => x.StartedAt).Take(100).ToListAsync(ct);
        var ids = runs.Select(r => r.Id).ToList();
        var recips = await db.ReportDistributionRecipients.AsNoTracking().Where(x => ids.Contains(x.DistributionId)).ToListAsync(ct);
        var uids = recips.Select(r => r.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var edIds = runs.Select(r => r.EditionId).OfType<Guid>().Distinct().ToList();
        var eds = await db.BoardReportEditions.AsNoTracking().Where(e => edIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.Version, ct);
        return runs.Select(r => new DistributionRow(r.Id, r.Trigger, r.Attempt, r.Status, r.DeliveryMode, r.StartedAt, r.CompletedAt, r.Recipients, r.Delivered, r.Excluded, r.Error, r.Note, r.EditionId,
            r.EditionId is { } e ? eds.GetValueOrDefault(e) : null, r.ArtifactId, r.NextAttemptAt,
            recips.Where(x => x.DistributionId == r.Id).OrderBy(x => x.Outcome).Select(x => new RecipientRow(x.UserId, names.GetValueOrDefault(x.UserId), x.Outcome, x.Reason, x.DownloadedAt)).ToList())).ToList();
    }

    /// <summary>Runs a schedule now (owner or administrator); does not move the regular next run.</summary>
    public async Task<DistributionRow> RunNowAsync(Guid id, CancellationToken ct)
    {
        var s = await OwnAsync(id, ct);
        var distId = await ExecuteInScopeAsync(s.Id, DistributionTrigger.Manual, m.UserId, ct);
        return (await HistoryAsync(s.Id, ct)).First(x => x.Id == distId);
    }

    // ---------- background ----------
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        await PurgeAsync(now, ct);
        var due = await db.ReportSchedules.AsNoTracking().Where(s => s.Enabled && s.PausedAt == null && ((s.RetryAt != null && s.RetryAt <= now) || (s.RetryAt == null && s.NextRunAt <= now)))
            .OrderBy(s => s.NextRunAt).Select(s => new { s.Id, Retry = s.RetryAt != null }).Take(200).ToListAsync(ct);
        foreach (var d in due)
        {
            try { await ExecuteInScopeAsync(d.Id, d.Retry ? DistributionTrigger.Retry : DistributionTrigger.Scheduled, null, ct); }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Scheduled report {Id} could not be processed", d.Id); }
        }
        return due.Count;
    }

    async Task PurgeAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.ReportArtifacts.Where(a => a.PurgedAt == null && a.ExpiresAt <= now).ExecuteUpdateAsync(u => u.SetProperty(a => a.Data, (byte[]?)null).SetProperty(a => a.PurgedAt, now), ct);

    /// <summary>Each run gets a fresh scope that acts as the schedule owner (so the report can never show more than the owner may see).</summary>
    async Task<Guid> ExecuteInScopeAsync(Guid scheduleId, DistributionTrigger trigger, Guid? triggeredBy, CancellationToken ct)
    {
        var owner = await db.ReportSchedules.AsNoTracking().Where(s => s.Id == scheduleId).Join(db.Users, s => s.OwnerId, u => u.Id, (s, u) => new { u.Id, u.DisplayName, u.IsActive }).FirstAsync(ct);
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<RunAsContext>().Set(owner.Id, $"{owner.DisplayName} (scheduled report)");
        return await scope.ServiceProvider.GetRequiredService<ReportDistributionService>().ExecuteAsync(scheduleId, trigger, triggeredBy, owner.IsActive, ct);
    }

    sealed record Content(string Title, string Link, Guid? EditionId, string Permission, bool AllSites, IReadOnlySet<Guid> Sites, bool Confidential, Func<ReportDeliveryMode, (byte[] Data, string Name, string Type)>? Render, string? Note);

    async Task<Guid> ExecuteAsync(Guid scheduleId, DistributionTrigger trigger, Guid? triggeredBy, bool ownerActive, CancellationToken ct)
    {
        var s = await db.ReportSchedules.FirstAsync(x => x.Id == scheduleId, ct);
        var now = clock.UtcNow;
        var run = new ReportDistribution { ScheduleId = s.Id, Trigger = trigger, Attempt = trigger == DistributionTrigger.Retry ? s.FailedAttempts + 1 : 1, DeliveryMode = s.DeliveryMode, StartedAt = now, TriggeredById = triggeredBy };
        db.ReportDistributions.Add(run);
        try
        {
            if (!ownerActive) throw new DistributionFailure("The schedule owner's account is inactive; the schedule was paused.", retry: false, pause: true);
            var content = await ContentAsync(s, run, ct);
            if (content is null) { Finish(s, run, trigger, DistributionStatus.Skipped); await db.SaveChangesAsync(ct); return run.Id; }
            run.EditionId = content.EditionId;
            var policy = await PolicyAsync(ct);

            // audience: named people + members of the recipient groups, then every permission check for this content
            var people = new HashSet<Guid>(s.Recipients);
            if (s.RecipientRoleIds.Count > 0) foreach (var u in await db.UserRoleAssignments.AsNoTracking().Where(a => s.RecipientRoleIds.Contains(a.RoleId)).Select(a => a.UserId).Distinct().ToListAsync(ct)) people.Add(u);
            var users = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).Select(u => new { u.Id, u.IsActive, u.PortalKind, u.Email }).ToListAsync(ct);
            var attach = s.DeliveryMode != ReportDeliveryMode.Link && content.Render is not null;
            var attachNote = (string?)null;
            if (attach && !ModeAllowed(policy, s.DeliveryMode)) { attach = false; attachNote = "Attachment not sent: the delivery policy no longer allows this attachment type; sent as a secure link."; }
            if (attach && content.Confidential && !policy.AllowConfidential) { attach = false; attachNote = "Attachment not sent: the report is confidential; sent as a secure link."; }
            if (attach && !await access.HasPermissionAsync(s.OwnerId, Permissions.ReportsExport, ct)) { attach = false; attachNote = "Attachment not sent: the owner no longer holds reports.export; sent as a secure link."; }
            var eligible = new List<Guid>();
            foreach (var u in users)
            {
                string? why = null;
                if (!u.IsActive) why = "Inactive account";
                else if (u.PortalKind is not null) why = "Portal account";
                else if (!await access.HasPermissionAsync(u.Id, Permissions.ReportsView, ct) && content.Permission != Permissions.EnterpriseView) why = "No reports.view permission";
                else
                {
                    var scope = await access.GetScopeAsync(u.Id, content.Permission, ct);
                    if (scope.IsEmpty) why = $"No {content.Permission} permission";
                    else if (content.EditionId is not null || attach)
                    {
                        // frozen content (edition or file) goes only to people whose access covers everything in it
                        var covers = scope.AllSites || (!content.AllSites && content.Sites.All(scope.SiteIds.Contains));
                        if (!covers) why = $"Access to {content.Permission} does not cover every site in the report";
                    }
                    if (why is null && attach && policy.AllowedEmailDomains.Count > 0 && !(u.Email is { } em && policy.AllowedEmailDomains.Any(d => em.EndsWith("@" + d, StringComparison.OrdinalIgnoreCase))))
                        why = "Email domain not allowed for attachments";
                }
                db.ReportDistributionRecipients.Add(new ReportDistributionRecipient { DistributionId = run.Id, UserId = u.Id, Outcome = why is null ? DistributionOutcome.Delivered : DistributionOutcome.Excluded, Reason = why });
                if (why is null) eligible.Add(u.Id);
            }
            foreach (var missing in people.Where(p => users.All(u => u.Id != p)))
                db.ReportDistributionRecipients.Add(new ReportDistributionRecipient { DistributionId = run.Id, UserId = missing, Outcome = DistributionOutcome.Excluded, Reason = "User no longer exists" });
            run.Recipients = people.Count; run.Delivered = eligible.Count; run.Excluded = people.Count - eligible.Count;
            if (eligible.Count == 0) throw new DistributionFailure("No recipient is allowed to receive this report (see the recipient list).", retry: true);

            ReportArtifact? artifact = null;
            if (attach)
            {
                var (data, name, type) = content.Render!(s.DeliveryMode);
                if (data.Length > policy.MaxAttachmentBytes) attachNote = $"Attachment not sent: {data.Length:N0} bytes is over the {policy.MaxAttachmentBytes:N0}-byte limit; sent as a secure link.";
                else
                {
                    artifact = new ReportArtifact { ScheduleId = s.Id, DistributionId = run.Id, FileName = name, ContentType = type, Data = data, Size = data.Length, Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                        Confidential = content.Confidential, CreatedAt = now, ExpiresAt = now.AddDays(policy.RetentionDays) };
                    db.ReportArtifacts.Add(artifact); run.ArtifactId = artifact.Id;
                }
            }
            run.Note = string.Join(" ", new[] { content.Note, attachNote }.Where(x => x is not null));
            if (run.Note.Length == 0) run.Note = null;
            var body = artifact is null
                ? $"Open the report to view it. You see only data you are permitted to see.{(attachNote is null ? "" : " " + attachNote)}"
                : $"The report is attached ({artifact.FileName}, available until {artifact.ExpiresAt:yyyy-MM-dd} UTC). Do not forward it outside the organization.";
            var link = artifact is null ? content.Link : $"/reports/deliveries/{artifact.Id}";
            foreach (var uid in eligible)
                db.Notifications.Add(new Notification { UserId = uid, CreatedAt = now, Title = $"Scheduled report: {content.Title}", Body = body, RecordType = artifact is null ? null : "ReportArtifact", RecordId = artifact?.Id,
                    Link = link, EmailRequested = true, AttachmentId = artifact?.Id });
            Finish(s, run, trigger, run.Excluded > 0 ? DistributionStatus.PartiallyDelivered : DistributionStatus.Succeeded);
        }
        catch (Exception e) when (e is DistributionFailure or AppException or FormatException or InvalidOperationException && e is not OperationCanceledException)
        {
            var f = e as DistributionFailure;
            run.Error = e.Message.Length > 2000 ? e.Message[..2000] : e.Message;
            run.Status = DistributionStatus.Failed; run.CompletedAt = clock.UtcNow;
            var retry = f?.Retry ?? true;
            var attempts = trigger == DistributionTrigger.Retry ? s.FailedAttempts + 1 : 1;
            if (f?.Pause == true) { s.PausedAt = clock.UtcNow; s.PauseReason = "Paused automatically: " + e.Message; }
            if (trigger != DistributionTrigger.Manual && retry && attempts < s.MaxAttempts && f?.Pause != true)
            {
                s.FailedAttempts = attempts; s.RetryAt = clock.UtcNow + Backoff[Math.Min(attempts - 1, Backoff.Length - 1)]; run.NextAttemptAt = s.RetryAt;
                s.LastStatus = $"Failed (attempt {attempts} of {s.MaxAttempts}), retrying: {run.Error}";
            }
            else
            {
                s.FailedAttempts = 0; s.RetryAt = null;
                if (trigger != DistributionTrigger.Manual) s.NextRunAt = NextInZone(s.Cron, s.TimeZone, clock.UtcNow);
                s.LastStatus = $"Failed: {run.Error}";
                // failure notification to the owner (manual runs show the error directly)
                if (trigger != DistributionTrigger.Manual)
                    db.Notifications.Add(new Notification { UserId = s.OwnerId, CreatedAt = clock.UtcNow, Title = $"Scheduled report failed: {s.Name ?? "report"}", Body = $"{run.Error} ({attempts} attempt(s)). The next regular run is {(s.PausedAt is null ? s.NextRunAt?.ToString("u") ?? "not scheduled" : "paused")}.",
                        Link = "/reports/schedules", EmailRequested = true });
            }
            s.LastRunAt = clock.UtcNow;
            log.LogWarning("Scheduled report {Id} failed: {Error}", s.Id, run.Error);
        }
        await db.SaveChangesAsync(ct);
        return run.Id;
    }

    void Finish(ReportSchedule s, ReportDistribution run, DistributionTrigger trigger, DistributionStatus status)
    {
        run.Status = status; run.CompletedAt = clock.UtcNow;
        s.LastRunAt = clock.UtcNow; s.FailedAttempts = 0; s.RetryAt = null;
        s.LastStatus = status switch { DistributionStatus.Succeeded => $"Delivered to {run.Delivered}", DistributionStatus.PartiallyDelivered => $"Delivered to {run.Delivered}, {run.Excluded} excluded", _ => $"Skipped: {run.Note}" };
        if (trigger != DistributionTrigger.Manual) s.NextRunAt = NextInZone(s.Cron, s.TimeZone, clock.UtcNow);
    }

    sealed class DistributionFailure(string message, bool retry, bool pause = false) : Exception(message) { public bool Retry => retry; public bool Pause => pause; }

    async Task<Content?> ContentAsync(ReportSchedule s, ReportDistribution run, CancellationToken ct)
    {
        if (s.Source == ReportScheduleSource.SavedReport)
        {
            var rep = await db.SavedReports.AsNoTracking().FirstOrDefaultAsync(x => x.Id == s.SavedReportId, ct) ?? throw new DistributionFailure("The saved report was deleted.", retry: false);
            var designer = scopesProvider.GetRequiredService<DesignerService>();
            var reports = scopesProvider.GetRequiredService<ReportService>();
            var def = (await reports.AvailableAsync(ct)).FirstOrDefault(d => d.Key == rep.ReportKey) ?? throw new DistributionFailure("The owner no longer has access to the underlying report.", retry: true);
            var result = await designer.RunReportAsync(rep.Id, ct);
            var ownerScope = await access.GetScopeAsync(s.OwnerId, def.Permission, ct);
            var sites = rep.SiteId is { } site ? new HashSet<Guid> { site } : ownerScope.SiteIds.Concat(ownerScope.DepartmentPairs.Select(p => p.Site)).ToHashSet();
            var rr = new ReportResult(def, result.Params, result.Columns, result.Rows, null);
            var stamp = clock.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            var safe = new string(rep.Name.Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
            return new Content(rep.Name, $"/platform/reports/{rep.Id}", null, def.Permission, rep.SiteId is null && ownerScope.AllSites, sites, rep.Confidential,
                mode => mode switch
                {
                    ReportDeliveryMode.XlsxAttachment => (ReportService.ToXlsx(rr, rep.Name), $"{safe}-{stamp}.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
                    ReportDeliveryMode.CsvAttachment => (Encoding.UTF8.GetBytes(ReportService.ToCsv(rr)), $"{safe}-{stamp}.csv", "text/csv"),
                    _ => (ReportService.ToPdf(rr, rep.Name), $"{safe}-{stamp}.pdf", "application/pdf"),
                }, null);
        }
        var kind = s.BoardReportKind!;
        string? note = null;
        if (s.GenerateDraftEdition)
        {
            var draft = await GenerateEditionCoreAsync(kind, s.OwnerId, s.Id, ct);
            var approvers = (await db.UserRoleAssignments.AsNoTracking().Select(a => a.UserId).Distinct().ToListAsync(ct)).Where(u => u != s.OwnerId).ToList();
            var notify = new List<Guid>();
            foreach (var u in approvers) if (await access.HasPermissionAsync(u, Permissions.ReportsApprove, ct)) notify.Add(u);
            await notifier.NotifyAsync(notify, $"Board report edition to approve: {draft.Title} v{draft.Version}", "A scheduled run generated a new edition. Review and approve it to release it for distribution.",
                new RecordInfo("BoardReportEdition", draft.Id, Guid.Empty, null, $"v{draft.Version}", draft.Title, null, Permissions.EnterpriseView, Permissions.ReportsApprove, $"/enterprise/board-editions/{draft.Id}"), true, ct);
            note = $"Draft edition v{draft.Version} generated for approval.";
        }
        var ed = await db.BoardReportEditions.AsNoTracking().Where(e => e.Kind == kind && e.Status == EditionStatus.Approved).OrderByDescending(e => e.Version).FirstOrDefaultAsync(ct);
        if (ed is null) { run.Note = (note is null ? "" : note + " ") + "No approved edition of this board report yet; nothing was sent."; return null; }
        return new Content($"{ed.Title} (edition {ed.Version})", $"/enterprise/board-editions/{ed.Id}", ed.Id, Permissions.EnterpriseView, ed.ScopeAllSites, ed.ScopeSiteIds.ToHashSet(), ed.Confidential,
            mode => mode == ReportDeliveryMode.PdfAttachment ? (EditionPdf(ed), $"board-{ed.Kind}-v{ed.Version}.pdf", "application/pdf") : (Encoding.UTF8.GetBytes(EditionCsv(ed)), $"board-{ed.Kind}-v{ed.Version}.csv", "text/csv"),
            note);
    }

    /// <summary>Services of the current (run-as) scope, so report generation sees the owner's identity.</summary>
    IServiceProvider scopesProvider => sp;

    // ---------- board report editions ----------
    async Task<BoardReportEdition> GenerateEditionCoreAsync(string kind, Guid generatedBy, Guid? scheduleId, CancellationToken ct)
    {
        if (!IntelligenceService.BoardReports.Contains(kind)) throw new AppException($"Board report must be one of: {string.Join(", ", IntelligenceService.BoardReports)}.");
        if (!await access.HasPermissionAsync(generatedBy, Permissions.EnterpriseView, ct)) throw new DistributionFailure("The schedule owner no longer holds enterprise.view.", retry: false);
        var intel = scopesProvider.GetRequiredService<IntelligenceService>();
        object data;
        try { data = await intel.BoardReportAsync(kind, ct); }
        catch (ForbiddenException e) { throw new DistributionFailure($"The schedule owner cannot generate this board report: {e.Message} (it draws on risks, suppliers and site metrics).", retry: false); }
        var json = JsonSerializer.Serialize(data, Json);
        var title = JsonDocument.Parse(json).RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? kind : kind;
        var scope = await access.GetScopeAsync(generatedBy, Permissions.EnterpriseView, ct);
        var version = (await db.BoardReportEditions.Where(e => e.Kind == kind).MaxAsync(e => (int?)e.Version, ct) ?? 0) + 1;
        var ed = new BoardReportEdition { Kind = kind, Version = version, Title = title, SnapshotJson = json, Status = EditionStatus.Draft, GeneratedById = generatedBy, GeneratedAt = clock.UtcNow,
            ScopeAllSites = scope.AllSites, ScopeSiteIds = scope.SiteIds.Concat(scope.DepartmentPairs.Select(p => p.Site)).Distinct().ToList(), ScheduleId = scheduleId };
        db.BoardReportEditions.Add(ed);
        await db.SaveChangesAsync(ct);
        return ed;
    }

    public async Task<EditionRow> GenerateEditionAsync(string kind, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.EnterpriseView, ct)) throw new ForbiddenException();
        var ed = await GenerateEditionCoreAsync(kind, m.UserId, null, ct);
        return (await EditionsAsync(kind, ct)).First(x => x.Id == ed.Id);
    }

    async Task<bool> CanSeeEditionAsync(BoardReportEdition e, CancellationToken ct)
    {
        var scope = await access.GetScopeAsync(m.UserId, Permissions.EnterpriseView, ct);
        return !scope.IsEmpty && (scope.AllSites || (!e.ScopeAllSites && e.ScopeSiteIds.All(scope.SiteIds.Contains)));
    }

    public async Task<List<EditionRow>> EditionsAsync(string? kind, CancellationToken ct)
    {
        var q = db.BoardReportEditions.AsNoTracking();
        if (kind is not null) q = q.Where(e => e.Kind == kind);
        var list = await q.OrderByDescending(e => e.GeneratedAt).Take(100).ToListAsync(ct);
        var visible = new List<BoardReportEdition>();
        foreach (var e in list) if (await CanSeeEditionAsync(e, ct)) visible.Add(e);
        var uids = visible.SelectMany(e => new[] { (Guid?)e.GeneratedById, e.ApprovedById }).OfType<Guid>().Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return visible.Select(e => new EditionRow(e.Id, e.Kind, e.Version, e.Title, e.Status, e.GeneratedAt, names.GetValueOrDefault(e.GeneratedById), e.ScopeAllSites, e.ScopeSiteIds.Count, e.ApprovedAt,
            e.ApprovedById is { } a ? names.GetValueOrDefault(a) : null, e.Notes, e.Confidential)).ToList();
    }

    async Task<BoardReportEdition> EditionEntityAsync(Guid id, CancellationToken ct)
    {
        var e = await db.BoardReportEditions.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Edition");
        if (!await CanSeeEditionAsync(e, ct)) throw new NotFoundException("Edition");
        return e;
    }

    public async Task<object> EditionAsync(Guid id, CancellationToken ct)
    {
        var e = await EditionEntityAsync(id, ct);
        var row = (await EditionsAsync(e.Kind, ct)).First(x => x.Id == id);
        return new { Edition = row, Snapshot = JsonDocument.Parse(e.SnapshotJson).RootElement.Clone() };
    }

    public async Task<byte[]> EditionPdfAsync(Guid id, CancellationToken ct) => EditionPdf(await EditionEntityAsync(id, ct));

    public async Task DecideEditionAsync(Guid id, bool approve, EditionDecision r, CancellationToken ct)
    {
        var e = await EditionEntityAsync(id, ct);
        if (!await m.CanAnyAsync(Permissions.ReportsApprove, ct)) throw new ForbiddenException("Approving board report editions needs reports.approve.");
        if (e.Status != EditionStatus.Draft) throw new ConflictException($"Edition {e.Version} is already {e.Status}.");
        if (e.GeneratedById == m.UserId) throw new ForbiddenException("The person who generated an edition cannot approve or reject it.");
        if (string.IsNullOrWhiteSpace(r.Notes) || r.Notes.Trim().Length < 5) throw new AppException("Notes are required (at least 5 characters).");
        e.Status = approve ? EditionStatus.Approved : EditionStatus.Rejected; e.ApprovedById = m.UserId; e.ApprovedAt = clock.UtcNow; e.Notes = r.Notes.Trim();
        await db.SaveChangesAsync(ct);
    }

    static byte[] EditionPdf(BoardReportEdition e)
    {
        var root = JsonDocument.Parse(e.SnapshotJson).RootElement;
        var pdf = new SimplePdf($"{e.Title} edition {e.Version}").Title($"{e.Title} — edition {e.Version}")
            .Field("Generated", e.GeneratedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Field("Status", e.Status.ToString())
            .Field("Approved", e.ApprovedAt?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)).Field("Classification", e.Confidential ? "CONFIDENTIAL" : "Internal");
        foreach (var p in root.EnumerateObject().Where(p => p.Name is not ("title" or "generated"))) Render(pdf, Label(p.Name), p.Value, 0);
        return pdf.ToBytes();

        static void Render(SimplePdf pdf, string name, JsonElement v, int depth)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.Object:
                    pdf.Heading(name);
                    foreach (var p in v.EnumerateObject())
                        if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array && depth < 2) Render(pdf, $"{name} › {Label(p.Name)}", p.Value, depth + 1);
                        else pdf.Field(Label(p.Name), Scalar(p.Value));
                    break;
                case JsonValueKind.Array:
                {
                    pdf.Heading(name);
                    var items = v.EnumerateArray().Take(50).ToList();
                    if (items.Count == 0) { pdf.Line("None."); break; }
                    if (items[0].ValueKind == JsonValueKind.Object)
                    {
                        var keys = items[0].EnumerateObject().Where(p => p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)).Select(p => p.Name).Take(6).ToList();
                        if (keys.Count == 0) { pdf.Line($"{items.Count} item(s)."); break; }
                        var w = 512.0 / keys.Count;
                        pdf.Table(keys.Select(k => (Label(k), w)).ToList(), items.Select(i => (IReadOnlyList<string?>)keys.Select(k => i.TryGetProperty(k, out var x) ? Scalar(x) : "").ToList()));
                    }
                    else pdf.Line(string.Join(", ", items.Select(Scalar)));
                    break;
                }
                default: pdf.Field(name, Scalar(v)); break;
            }
        }
    }

    static string EditionCsv(BoardReportEdition e)
    {
        var sb = new StringBuilder("section,field,value\n");
        void Walk(string path, JsonElement v)
        {
            if (v.ValueKind == JsonValueKind.Object) foreach (var p in v.EnumerateObject()) Walk(path.Length == 0 ? p.Name : path + "." + p.Name, p.Value);
            else if (v.ValueKind == JsonValueKind.Array) { var i = 0; foreach (var x in v.EnumerateArray()) Walk($"{path}[{i++}]", x); }
            else { var dot = path.LastIndexOf('.'); sb.Append(Csv(dot < 0 ? "" : path[..dot])).Append(',').Append(Csv(dot < 0 ? path : path[(dot + 1)..])).Append(',').Append(Csv(Scalar(v))).Append('\n'); }
        }
        Walk("", JsonDocument.Parse(e.SnapshotJson).RootElement);
        return sb.ToString();
        static string Csv(string s) { if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s; return s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
    }

    static string Scalar(JsonElement v) => v.ValueKind switch { JsonValueKind.String => v.GetString() ?? "", JsonValueKind.Null or JsonValueKind.Undefined => "", JsonValueKind.Object or JsonValueKind.Array => "…", _ => v.GetRawText() };
    static string Label(string key) => key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + string.Concat(key[1..].Select(ch => char.IsUpper(ch) ? " " + char.ToLowerInvariant(ch) : ch.ToString()));

    // ---------- artifacts ----------
    public async Task<(byte[] Data, string Name, string Type)> ArtifactAsync(Guid id, CancellationToken ct)
    {
        var a = await db.ReportArtifacts.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Report file");
        var recipient = await db.ReportDistributionRecipients.FirstOrDefaultAsync(r => r.DistributionId == a.DistributionId && r.UserId == m.UserId && r.Outcome == DistributionOutcome.Delivered, ct);
        var owner = await db.ReportSchedules.AnyAsync(s => s.Id == a.ScheduleId && s.OwnerId == m.UserId, ct);
        if (recipient is null && !owner) throw new NotFoundException("Report file");
        if (a.Data is null || a.ExpiresAt <= clock.UtcNow) throw new AppException("This report file has expired under the retention policy. Open the report itself instead.", 410);
        if (recipient is not null && recipient.DownloadedAt is null) { recipient.DownloadedAt = clock.UtcNow; await db.SaveChangesAsync(ct); }
        return (a.Data, a.FileName, a.ContentType);
    }

    /// <summary>For the email job: the attachment for a notification if it may still be sent (not expired, TLS required for attachments).</summary>
    public static async Task<ReportArtifact?> EmailAttachmentAsync(AppDbContext db, Guid? attachmentId, DateTimeOffset now, CancellationToken ct) =>
        attachmentId is { } id ? await db.ReportArtifacts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.Data != null && a.ExpiresAt > now, ct) : null;
}
