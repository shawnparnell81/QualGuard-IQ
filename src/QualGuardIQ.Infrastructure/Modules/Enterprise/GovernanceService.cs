using QualGuardIQ.Application.Records;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;
using QualGuardIQ.Infrastructure.Storage;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

public sealed record RetentionPolicyInput(string Name, string RecordType, string? ProductFamily, string? RegulatoryCategory, int RetainYears, RetentionAction Action, bool IsActive = true);
public sealed record RetentionDue(string RecordType, Guid RecordId, string Number, DateTimeOffset CreatedAt, Guid SiteId);
public sealed record RetentionDecisionInput(string RecordType, Guid RecordId, RetentionDecisionKind Decision, string Reason);
public sealed record ContinuityInput(ContinuityKind Kind, string Title, Guid SiteId, DateOnly PlannedOn, int? TargetRecoveryMinutes);
public sealed record ContinuityResultInput(ContinuityResult Result, int? RecoveryMinutes, int? DataLossMinutes, string? Findings);
public sealed record ChannelInput(string Name, NotificationChannelKind Kind, NotificationScope Scope, Guid? OrgUnitId, Guid? SiteId, string Target, List<string>? Keywords);
public sealed record BroadcastInput(NotificationScope Scope, Guid? OrgUnitId, Guid? SiteId, string Title, string? Body);
public sealed record ApiClientInput(string Name, Guid ServiceUserId, int RateLimitPerMinute, DateOnly? ExpiresOn);
public sealed record ApiClientCreated(Guid Id, string ClientId, string Secret, string ApiKey);

/// <summary>Retention governance, business continuity exercises, scoped notification channels and broadcasts, public API clients and the Part 11 readiness check.</summary>
public sealed class GovernanceService(AppDbContext db, ModuleSupport m, INumberingService numbers, IFileStorage storage, IHttpClientFactory http, INotifier notifier,
    IOptions<IdentityOptions> identity, UserManager<ApplicationUser> users)
{
    public const string ContinuityType = "ContinuityExercise";

    // ---------- retention ----------
    /// <summary>Record types retention can evaluate (age is taken from the record's creation time).</summary>
    public static readonly string[] RetentionTypes = ["Ncr", "Capa", "Audit", "Complaint", "FormRecord", "Inspection", "Fai", "ChangeRequest", "CalibrationEvent", "Validation", "Ppap", "TrainingAssignment"];

    IQueryable<RetentionDue> Source(string type) => type switch
    {
        "Ncr" => db.Ncrs.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "Capa" => db.Capas.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "Audit" => db.Audits.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "Complaint" => db.Complaints.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "FormRecord" => db.FormRecords.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "Inspection" => db.Inspections.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "Fai" => db.Fais.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "ChangeRequest" => db.ChangeRequests.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "CalibrationEvent" => db.CalibrationEvents.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "Validation" => db.ValidationProjects.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "Ppap" => db.PpapSubmissions.Select(x => new RetentionDue(type, x.Id, x.Number, x.CreatedAt, x.SiteId)),
        "TrainingAssignment" => db.TrainingAssignments.Select(x => new RetentionDue(type, x.Id, "TA-" + x.Id.ToString().Substring(0, 8), x.CreatedAt, x.SiteId)),
        _ => throw new AppException($"Retention can evaluate: {string.Join(", ", RetentionTypes)}.")
    };

    public async Task<RetentionPolicy> SavePolicyAsync(Guid? id, RetentionPolicyInput r, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.GovernanceEdit, ct)) throw new ForbiddenException();
        ModuleSupport.Required(r.Name, "Name", 200);
        if (!RetentionTypes.Contains(r.RecordType)) throw new AppException($"Retention can evaluate: {string.Join(", ", RetentionTypes)}.");
        if (r.RetainYears is < 1 or > 100) throw new AppException("Retention must be 1–100 years.");
        var p = id is { } pid ? await db.RetentionPolicies.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw new NotFoundException("Retention policy") : new RetentionPolicy();
        p.Name = r.Name.Trim(); p.RecordType = r.RecordType; p.ProductFamily = r.ProductFamily?.Trim(); p.RegulatoryCategory = r.RegulatoryCategory?.Trim(); p.RetainYears = r.RetainYears; p.Action = r.Action; p.IsActive = r.IsActive;
        if (id is null) db.RetentionPolicies.Add(p);
        await db.SaveChangesAsync(ct);
        return p;
    }

    public async Task<List<RetentionPolicy>> PoliciesAsync(CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.GovernanceView, ct)) throw new ForbiddenException();
        return await db.RetentionPolicies.AsNoTracking().OrderBy(p => p.RecordType).ThenBy(p => p.Name).ToListAsync(ct);
    }

    /// <summary>Records past the policy's retention period that have no decision yet (limited to sites the caller governs).</summary>
    public async Task<List<RetentionDue>> DueAsync(Guid policyId, CancellationToken ct)
    {
        var p = await db.RetentionPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == policyId, ct) ?? throw new NotFoundException("Retention policy");
        var cutoff = m.Now.AddYears(-p.RetainYears);
        var due = await Source(p.RecordType).Where(x => x.CreatedAt < cutoff && !db.RetentionDecisions.Any(d => d.RecordType == p.RecordType && d.RecordId == x.RecordId && d.Decision != RetentionDecisionKind.Extended))
            .OrderBy(x => x.CreatedAt).Take(500).ToListAsync(ct);
        var result = new List<RetentionDue>();
        foreach (var d in due) if (await m.CanAsync(Permissions.GovernanceView, d.SiteId, null, ct)) result.Add(d);
        return result;
    }

    public async Task DecideAsync(Guid policyId, RetentionDecisionInput r, CancellationToken ct)
    {
        var p = await db.RetentionPolicies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == policyId, ct) ?? throw new NotFoundException("Retention policy");
        if (r.RecordType != p.RecordType) throw new AppException("The record type does not match the policy.");
        ModuleSupport.Required(r.Reason, "Reason", 2000);
        var rec = await Source(p.RecordType).FirstOrDefaultAsync(x => x.RecordId == r.RecordId, ct) ?? throw new NotFoundException("Record");
        await m.RequireAsync(Permissions.GovernanceEdit, rec.SiteId, null, ct);
        if (r.Decision == RetentionDecisionKind.DisposalApproved && rec.CreatedAt >= m.Now.AddYears(-p.RetainYears)) throw new AppException("The record has not reached the end of its retention period.");
        db.RetentionDecisions.Add(new RetentionDecision { PolicyId = p.Id, RecordType = p.RecordType, RecordId = rec.RecordId, RecordNumber = rec.Number, Decision = r.Decision, Reason = r.Reason.Trim(), DecidedById = m.UserId, DecidedAt = m.Now });
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<RetentionDecision>> DecisionsAsync(CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.GovernanceView, ct)) throw new ForbiddenException();
        return await db.RetentionDecisions.AsNoTracking().OrderByDescending(d => d.DecidedAt).Take(500).ToListAsync(ct);
    }

    // ---------- business continuity ----------
    public async Task<ContinuityExercise> PlanExerciseAsync(ContinuityInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.GovernanceEdit, r.SiteId, null, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var e = new ContinuityExercise { Number = await numbers.NextAsync(ContinuityType, r.SiteId, ct), Kind = r.Kind, Title = r.Title.Trim(), SiteId = r.SiteId, PlannedOn = r.PlannedOn, TargetRecoveryMinutes = r.TargetRecoveryMinutes };
        db.ContinuityExercises.Add(e);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return e;
    }

    public async Task<List<ContinuityExercise>> ExercisesAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.ContinuityExercises.AsNoTracking(), Permissions.GovernanceView, ct)).OrderByDescending(e => e.PlannedOn).Take(500).ToListAsync(ct);

    /// <summary>Live readiness checks: database round trip, file store reachable, age of the newest audit entry.</summary>
    public async Task<Dictionary<string, object?>> SystemChecksAsync(CancellationToken ct)
    {
        var checks = new Dictionary<string, object?>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await db.Sites.AnyAsync(ct); checks["database"] = new { ok = true, ms = sw.ElapsedMilliseconds }; } catch (Exception ex) { checks["database"] = new { ok = false, error = ex.GetType().Name }; }
        sw.Restart();
        try { await storage.EnsureReadyAsync(ct); checks["fileStore"] = new { ok = true, ms = sw.ElapsedMilliseconds }; } catch (Exception ex) { checks["fileStore"] = new { ok = false, error = ex.GetType().Name }; }
        var last = await db.AuditEntries.AsNoTracking().OrderByDescending(a => a.Id).Select(a => (DateTimeOffset?)a.OccurredAt).FirstOrDefaultAsync(ct);
        checks["auditTrail"] = new { ok = last is not null, lastEntryAt = last };
        checks["checkedAt"] = m.Now;
        return checks;
    }

    public async Task RecordResultAsync(Guid id, ContinuityResultInput r, CancellationToken ct)
    {
        var e = await db.ContinuityExercises.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Exercise");
        await m.RequireAsync(Permissions.GovernanceEdit, e.SiteId, null, ct);
        if (e.Result != ContinuityResult.Planned) throw new ConflictException("The result is already recorded; plan a new exercise for a re-test.");
        if (r.Result == ContinuityResult.Planned) throw new AppException("Choose passed, passed with issues or failed.");
        if (r.Result != ContinuityResult.Passed && string.IsNullOrWhiteSpace(r.Findings)) throw new AppException("Describe the findings.");
        e.Result = r.Result; e.RecoveryMinutes = r.RecoveryMinutes; e.DataLossMinutes = r.DataLossMinutes; e.Findings = r.Findings?.Trim(); e.PerformedAt = m.Now; e.PerformedById = m.UserId;
        e.SystemChecks = System.Text.Json.JsonSerializer.Serialize(await SystemChecksAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    // ---------- notification channels and scoped broadcasts ----------
    async Task RequireApiAdminAsync(CancellationToken ct) { if (!await m.CanAnyAsync(Permissions.ApiAdmin, ct)) throw new ForbiddenException(); }

    public async Task<NotificationChannel> SaveChannelAsync(Guid? id, ChannelInput r, CancellationToken ct)
    {
        await RequireApiAdminAsync(ct);
        ModuleSupport.Required(r.Name, "Name", 200); ModuleSupport.Required(r.Target, "Target", 1000);
        if (r.Scope == NotificationScope.Site && r.SiteId is null) throw new AppException("A site channel needs the site.");
        if (r.Scope == NotificationScope.Region && r.OrgUnitId is null) throw new AppException("A regional channel needs the region.");
        if (r.Kind is NotificationChannelKind.Teams or NotificationChannelKind.Sms or NotificationChannelKind.Mobile && !(Uri.TryCreate(r.Target, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps))
            throw new AppException("Teams, SMS gateway and push targets must be HTTPS URLs.");
        var c = id is { } cid ? await db.NotificationChannels.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw new NotFoundException("Channel") : new NotificationChannel();
        var targetChanged = c.Target != r.Target.Trim();
        c.Name = r.Name.Trim(); c.Kind = r.Kind; c.Scope = r.Scope; c.OrgUnitId = r.OrgUnitId; c.SiteId = r.SiteId; c.Target = r.Target.Trim(); c.Keywords = r.Keywords ?? [];
        if (targetChanged) { c.Enabled = false; c.LastTestOk = null; }
        if (id is null) db.NotificationChannels.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task<List<NotificationChannel>> ChannelsAsync(CancellationToken ct) { await RequireApiAdminAsync(ct); return await db.NotificationChannels.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct); }

    async Task<bool> SendAsync(NotificationChannel c, string title, string? body, CancellationToken ct)
    {
        if (c.Kind == NotificationChannelKind.Email) return c.Target.Split(',', ';').All(a => System.Net.Mail.MailAddress.TryCreate(a.Trim(), out _));
        try
        {
            object payload = c.Kind == NotificationChannelKind.Teams ? new { text = $"**{title}**\n\n{body}" } : new { title, body, channel = c.Name };
            using var resp = await http.CreateClient("notify").PostAsJsonAsync(c.Target, payload, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    public async Task<bool> TestChannelAsync(Guid id, bool enableOnSuccess, CancellationToken ct)
    {
        await RequireApiAdminAsync(ct);
        var c = await db.NotificationChannels.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Channel");
        var ok = await SendAsync(c, "QualGuard IQ test message", $"Channel {c.Name} is connected.", ct);
        c.LastTestedAt = m.Now; c.LastTestOk = ok;
        if (ok && enableOnSuccess) c.Enabled = true;
        await db.SaveChangesAsync(ct);
        return ok;
    }

    public async Task DisableChannelAsync(Guid id, CancellationToken ct)
    {
        await RequireApiAdminAsync(ct);
        var c = await db.NotificationChannels.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Channel");
        c.Enabled = false;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Site, regional or corporate notice: in-app notification to every active user of the sites in scope, plus every enabled channel whose scope covers it.</summary>
    public async Task<object> BroadcastAsync(BroadcastInput r, CancellationToken ct)
    {
        ModuleSupport.Required(r.Title, "Title", 200);
        var units = await db.OrgUnits.AsNoTracking().Where(u => !u.IsArchived).ToListAsync(ct);
        List<Guid> sites = r.Scope switch
        {
            NotificationScope.Site => [r.SiteId ?? throw new AppException("Choose the site.")],
            NotificationScope.Region => EnterpriseService.SitesUnder(units, r.OrgUnitId ?? throw new AppException("Choose the region.")),
            _ => await db.Sites.Where(s => !s.IsArchived).Select(s => s.Id).ToListAsync(ct)
        };
        foreach (var s in sites) await m.RequireAsync(Permissions.EnterpriseEdit, s, null, ct);
        var people = await db.Users.Where(u => u.IsActive && u.PortalKind == null && (sites.Contains(u.DefaultSiteId ?? Guid.Empty) || db.UserRoleAssignments.Any(a => a.UserId == u.Id && a.SiteId != null && sites.Contains(a.SiteId.Value))))
            .Select(u => u.Id).ToListAsync(ct);
        await notifier.NotifyAsync(people, r.Title.Trim(), r.Body?.Trim(), null, ct: ct);
        var channels = await db.NotificationChannels.Where(c => c.Enabled).ToListAsync(ct);
        var sent = 0;
        foreach (var c in channels)
        {
            var covers = c.Scope switch
            {
                NotificationScope.Corporate => true,
                NotificationScope.Region => c.OrgUnitId is { } ou && EnterpriseService.SitesUnder(units, ou).Intersect(sites).Any(),
                _ => c.SiteId is { } cs && sites.Contains(cs)
            };
            if (!covers || (c.Keywords.Count > 0 && !c.Keywords.Any(k => r.Title.Contains(k, StringComparison.OrdinalIgnoreCase)))) continue;
            if (await SendAsync(c, r.Title, r.Body, ct)) sent++;
        }
        return new { Recipients = people.Count, Channels = sent, Sites = sites.Count };
    }

    // ---------- public API clients ----------
    public static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    public async Task<ApiClientCreated> CreateClientAsync(ApiClientInput r, CancellationToken ct)
    {
        await RequireApiAdminAsync(ct);
        ModuleSupport.Required(r.Name, "Name", 200);
        if (r.RateLimitPerMinute is < 1 or > 6000) throw new AppException("Rate limit must be 1–6000 requests per minute.");
        var svc = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == r.ServiceUserId, ct) ?? throw new AppException("Select the service user the client acts as.");
        if (!svc.IsActive || svc.PortalKind is not null) throw new AppException("The service user must be an active staff account.");
        var clientId = "qg_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var c = new ApiClient { Name = r.Name.Trim(), ClientId = clientId, SecretHash = Hash(secret), SecretHint = secret[^4..], ServiceUserId = r.ServiceUserId, RateLimitPerMinute = r.RateLimitPerMinute, ExpiresOn = r.ExpiresOn };
        db.ApiClients.Add(c);
        await db.SaveChangesAsync(ct);
        return new ApiClientCreated(c.Id, clientId, secret, $"{clientId}.{secret}");
    }

    public async Task<List<object>> ClientsAsync(CancellationToken ct)
    {
        await RequireApiAdminAsync(ct);
        return (await db.ApiClients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct)).Select(c => (object)new { c.Id, c.Name, c.ClientId, c.SecretHint, c.ServiceUserId, c.RateLimitPerMinute, c.Enabled, c.ExpiresOn, c.LastUsedAt, c.CreatedAt }).ToList();
    }

    public async Task SetClientEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        await RequireApiAdminAsync(ct);
        var c = await db.ApiClients.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("API client");
        c.Enabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ApiClientCreated> RotateAsync(Guid id, CancellationToken ct)
    {
        await RequireApiAdminAsync(ct);
        var c = await db.ApiClients.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("API client");
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        c.SecretHash = Hash(secret); c.SecretHint = secret[^4..];
        await db.SaveChangesAsync(ct);
        return new ApiClientCreated(c.Id, c.ClientId, secret, $"{c.ClientId}.{secret}");
    }

    // ---------- Part 11 ----------
    /// <summary>Configuration evidence for 21 CFR Part 11 controls, plus a re-computation of every electronic signature hash.</summary>
    public async Task<object> Part11Async(CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.AuditTrailView, ct)) throw new ForbiddenException();
        var o = identity.Value;
        var decisions = await db.ApprovalDecisions.AsNoTracking().Where(d => d.SignatureHash != null).ToListAsync(ct);
        var numbersById = await db.ApprovalRequests.AsNoTracking().Where(r => decisions.Select(d => d.RequestId).Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.RecordNumber, ct);
        var broken = decisions.Where(d => WorkflowService.SignatureHash(d, numbersById.GetValueOrDefault(d.RequestId, "")) != d.SignatureHash).Select(d => new { d.Id, d.RequestId, d.SignerName, d.SignedAt }).ToList();
        var mfaUsers = await db.Users.CountAsync(u => u.IsActive && u.TwoFactorEnabled, ct);
        var activeUsers = await db.Users.CountAsync(u => u.IsActive, ct);
        var mfaSteps = await db.ApprovalRouteSteps.CountAsync(s => s.RequireMfa, ct);
        var routes = await db.ApprovalRoutes.CountAsync(r => r.IsActive, ct);
        var checks = new List<object>
        {
            new { Control = "11.10(e) Audit trails", Ok = await db.AuditEntries.AnyAsync(ct), Evidence = "Every change to audited records is written with user, time, action and description; the trail is append-only in the application." },
            new { Control = "11.10(d) Limiting system access", Ok = true, Evidence = $"Role-based permissions scoped by site; {await db.QmsRoles.CountAsync(ct)} roles configured." },
            new { Control = "11.10(g) Authority checks", Ok = routes > 0, Evidence = $"{routes} active approval routes; each step requires a permission and optionally a current qualification." },
            new { Control = "11.50 Signature manifestations", Ok = true, Evidence = "Each signature stores the signer's printed name, date/time and meaning (approval, review, verification, release as configured per step)." },
            new { Control = "11.70 Signature/record linking", Ok = broken.Count == 0, Evidence = $"{decisions.Count} signatures hashed to their record; {broken.Count} failed re-computation." },
            new { Control = "11.200 Signature components", Ok = true, Evidence = $"Signing re-enters the password every time; MFA code required when the signer has MFA ({mfaUsers} of {activeUsers} active users) and on {mfaSteps} route steps that demand it." },
            new { Control = "11.300 Password controls", Ok = o.Password.RequiredLength >= 8 && o.Lockout.MaxFailedAccessAttempts <= 10, Evidence = $"Minimum length {o.Password.RequiredLength}; lockout after {o.Lockout.MaxFailedAccessAttempts} failures for {o.Lockout.DefaultLockoutTimeSpan.TotalMinutes} min." },
            new { Control = "11.10(c) Protection of records / version history", Ok = true, Evidence = "Released revisions are content-locked; changes create new revisions; ledger records are append-only." },
            new { Control = "11.10(a) Validation of systems", Ok = await db.ValidationProjects.AnyAsync(v => v.Kind == ValidationKind.Software && v.Status == ValidationStatus.Released, ct), Evidence = "A released software validation record for this system is expected (Validation module)." }
        };
        return new { Checks = checks, BrokenSignatures = broken, GeneratedAt = m.Now };
    }
}
