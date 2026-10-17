using QualGuardIQ.Application.Records;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

public sealed record QualificationResult(bool Qualified, string Status, string Reason, DateOnly? ExpiresOn);

/// <summary>
/// Employee qualification control. A person holds a skill when every linked course is completed and in date
/// (an open retraining assignment past its due date counts against them) and, where required, a current certification exists.
/// Used before approvals (route step RequiredSkillId), auditor assignment and work assignment checks.
/// </summary>
public sealed class QualificationService(AppDbContext db, ISiteCalendar calendar)
{
    public async Task<QualificationResult> CheckAsync(Guid userId, Guid skillId, CancellationToken ct)
    {
        var skill = await db.Skills.AsNoTracking().FirstOrDefaultAsync(x => x.Id == skillId, ct) ?? throw new NotFoundException("Skill");
        return await CheckAsync(userId, skill, ct);
    }

    public async Task<QualificationResult> CheckAsync(Guid userId, Skill skill, CancellationToken ct)
    {
        var map = await CheckManyAsync([userId], [skill], ct);
        return map[(userId, skill.Id)];
    }

    /// <summary>Bulk evaluation used by the skills matrix (one query per table).</summary>
    public async Task<Dictionary<(Guid User, Guid Skill), QualificationResult>> CheckManyAsync(IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Skill> skills, CancellationToken ct)
    {
        var courseIds = skills.SelectMany(s => s.CourseIds).Distinct().ToList();
        var skillIds = skills.Select(s => s.Id).ToList();
        var assignments = await db.TrainingAssignments.AsNoTracking()
            .Where(a => userIds.Contains(a.UserId) && courseIds.Contains(a.CourseId) && (a.Status == AssignmentStatus.Completed || a.Status == AssignmentStatus.Assigned))
            .Select(a => new { a.UserId, a.CourseId, a.Status, a.CompletedAt, a.ExpiresOn, a.DueOn, a.Reason }).ToListAsync(ct);
        var certs = await db.EmployeeCertifications.AsNoTracking().Where(c => userIds.Contains(c.UserId) && skillIds.Contains(c.SkillId) && c.Status == CertificationStatus.Active).ToListAsync(ct);
        var courses = await db.TrainingCourses.AsNoTracking().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Number, ct);
        var active = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.IsActive, ct);
        var auditCounts = skills.Any(s => s.MinimumAudits > 0)
            ? await db.Audits.AsNoTracking().Where(a => userIds.Contains(a.LeadAuditorId) && a.Status == AuditStatus.Closed).GroupBy(a => a.LeadAuditorId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct)
            : [];
        var result = new Dictionary<(Guid, Guid), QualificationResult>();
        foreach (var skill in skills)
        {
            var today = await calendar.TodayAsync(skill.SiteId, ct);
            foreach (var uid in userIds)
            {
                result[(uid, skill.Id)] = Evaluate();
                QualificationResult Evaluate()
                {
                    if (!active.GetValueOrDefault(uid)) return new(false, "Inactive", "The person is not an active user.", null);
                    DateOnly? expires = null;
                    var dueSoon = false;
                    foreach (var cid in skill.CourseIds)
                    {
                        var mine = assignments.Where(a => a.UserId == uid && a.CourseId == cid).ToList();
                        var done = mine.Where(a => a.Status == AssignmentStatus.Completed).OrderByDescending(a => a.CompletedAt).FirstOrDefault();
                        var label = courses.GetValueOrDefault(cid, cid.ToString());
                        if (done is null) return new(false, "Missing", $"Lacks qualification: course {label} is not completed.", null);
                        if (done.ExpiresOn is { } e && e < today) return new(false, "Expired", $"Qualification expired: course {label} lapsed on {e:yyyy-MM-dd}.", e);
                        var open = mine.FirstOrDefault(a => a.Status == AssignmentStatus.Assigned);
                        if (open is not null && open.DueOn < today) return new(false, "RetrainingOverdue", $"Retraining on course {label} was due {open.DueOn:yyyy-MM-dd}.", done.ExpiresOn);
                        if (open is not null) dueSoon = true;
                        if (done.ExpiresOn is { } e2 && (expires is null || e2 < expires)) expires = done.ExpiresOn;
                    }
                    if (skill.RequiresCertification || skill.CourseIds.Count == 0)
                    {
                        var cert = certs.Where(c => c.UserId == uid && c.SkillId == skill.Id).OrderByDescending(c => c.ExpiresOn ?? DateOnly.MaxValue).FirstOrDefault();
                        if (cert is null) return new(false, "Missing", $"Lacks required certification for {skill.Code}.", null);
                        if (cert.ExpiresOn is { } ce && ce < today) return new(false, "Expired", $"Certification for {skill.Code} expired on {ce:yyyy-MM-dd}.", ce);
                        if (cert.ExpiresOn is { } ce2 && (expires is null || ce2 < expires)) expires = cert.ExpiresOn;
                    }
                    if (skill.MinimumAudits > 0 && auditCounts.GetValueOrDefault(uid) < skill.MinimumAudits)
                        return new(false, "Missing", $"Needs {skill.MinimumAudits} completed audits as lead (has {auditCounts.GetValueOrDefault(uid)}).", expires);
                    if (expires is { } x && x <= today.AddDays(30)) return new(true, "Expiring", $"Qualified until {x:yyyy-MM-dd}.", x);
                    return new(true, dueSoon ? "RetrainingDue" : "Qualified", dueSoon ? "Qualified; retraining is assigned." : "Qualified.", expires);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Auditor qualification: when the organization has auditor qualifications covering the audit type, the person must hold one of them.
    /// With none configured for the type, assignment is allowed (configuration decides).
    /// </summary>
    public async Task<QualificationResult> CheckAuditorAsync(Guid userId, string auditType, Guid siteId, CancellationToken ct)
    {
        var quals = (await db.Skills.AsNoTracking().Where(s => s.Kind == SkillKind.AuditorQualification && !s.IsArchived && s.SiteId == siteId).ToListAsync(ct))
            .Where(s => s.AuditTypes.Count == 0 || s.AuditTypes.Contains(auditType, StringComparer.OrdinalIgnoreCase)).ToList();
        if (quals.Count == 0) return new(true, "NotConfigured", $"No auditor qualification is configured for {auditType} audits.", null);
        var results = await CheckManyAsync([userId], quals, ct);
        var ok = results.Values.FirstOrDefault(r => r.Qualified);
        return ok ?? results.Values.First() with { Reason = $"Not a qualified {auditType} auditor: " + results.Values.First().Reason };
    }

    public async Task RequireAuditorAsync(Guid userId, string auditType, Guid siteId, string role, CancellationToken ct)
    {
        var r = await CheckAuditorAsync(userId, auditType, siteId, ct);
        if (!r.Qualified)
        {
            var name = await db.Users.Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct);
            throw new AppException($"{name} cannot be the {role}. {r.Reason}");
        }
    }
}

public sealed record SkillInput(string Code, string Name, SkillKind Kind, Guid SiteId, Guid? DepartmentId, List<Guid>? CourseIds, bool RequiresCertification, List<string>? AuditTypes, int MinimumAudits);
public sealed record SkillRequirementInput(Guid SiteId, Guid? DepartmentId, Guid? WorkCenterId);
public sealed record CertificationInput(Guid UserId, DateOnly CertifiedOn, DateOnly? ExpiresOn, string? Reference, string? IssuedBy);
public sealed record CurriculumInput(string Title, CurriculumKind Kind, Guid SiteId, Guid? DepartmentId, List<Guid> CourseIds, bool AutoAssign, Guid? GrantsSkillId, int RefresherLeadDays = 30);
public sealed record MatrixCell(Guid UserId, Guid SkillId, string Status, bool Qualified, string Reason, DateOnly? ExpiresOn);
public sealed record MatrixPerson(Guid Id, string Name, Guid? DepartmentId);
public sealed record SkillsMatrix(List<Skill> Skills, List<MatrixPerson> People, List<MatrixCell> Cells, Dictionary<Guid, List<Guid>> RequiredBy);
public sealed record QualificationGap(Guid UserId, string Person, Guid SkillId, string Skill, string Status, string Reason, DateOnly? ExpiresOn);

/// <summary>Skills, certifications, curricula (paths, certification and qualification programs), the skills matrix and automatic assignment. Extends training.</summary>
public sealed class WorkforceService(AppDbContext db, ModuleSupport m, INumberingService numbers, QualificationService q, TrainingService training) : ITrainingCompletedHandler
{
    public const string SkillType = "Skill", CurriculumType = "TrainingCurriculum";

    async Task ValidateCoursesAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Distinct().Count() != ids.Count) throw new AppException("A course is listed twice.");
        if (await db.TrainingCourses.CountAsync(c => ids.Contains(c.Id), ct) != ids.Count) throw new AppException("Select existing training courses.");
    }

    public async Task<Skill> CreateSkillAsync(SkillInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.TrainingEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Code, "Code", 40); ModuleSupport.Required(r.Name, "Name", 200);
        if (r.MinimumAudits is < 0 or > 100) throw new AppException("Minimum audits must be between 0 and 100.");
        await ValidateCoursesAsync(r.CourseIds ?? [], ct);
        var code = r.Code.Trim().ToUpperInvariant();
        if (await db.Skills.AnyAsync(s => s.Code == code && s.SiteId == r.SiteId, ct)) throw new ConflictException("That skill code already exists at this site.");
        var s = new Skill { Code = r.Code.Trim().ToUpperInvariant(), Name = r.Name.Trim(), Kind = r.Kind, SiteId = r.SiteId, DepartmentId = r.DepartmentId, CourseIds = r.CourseIds ?? [],
            RequiresCertification = r.RequiresCertification, AuditTypes = r.AuditTypes ?? [], MinimumAudits = r.MinimumAudits };
        db.Skills.Add(s);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(SkillType, s.Id, s.Name, ct);
        return s;
    }

    public async Task UpdateSkillAsync(Guid id, SkillInput r, CancellationToken ct)
    {
        await m.EditAsync(SkillType, id, ct);
        var s = await db.Skills.FirstAsync(x => x.Id == id, ct);
        ModuleSupport.Required(r.Name, "Name", 200);
        await ValidateCoursesAsync(r.CourseIds ?? [], ct);
        s.Name = r.Name.Trim(); s.Kind = r.Kind; s.CourseIds = r.CourseIds ?? []; s.RequiresCertification = r.RequiresCertification; s.AuditTypes = r.AuditTypes ?? []; s.MinimumAudits = r.MinimumAudits;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Skill>> SkillsAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.Skills.AsNoTracking(), Permissions.TrainingView, ct)).Where(s => !s.IsArchived).OrderBy(s => s.Code).ToListAsync(ct);

    public async Task AddRequirementAsync(Guid skillId, SkillRequirementInput r, CancellationToken ct)
    {
        await m.EditAsync(SkillType, skillId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        if (r.WorkCenterId is { } w && !await db.WorkCenters.AnyAsync(x => x.Id == w && x.SiteId == r.SiteId, ct)) throw new AppException("Select a production line at that site.");
        if (await db.SkillRequirements.AnyAsync(x => x.SkillId == skillId && x.SiteId == r.SiteId && x.DepartmentId == r.DepartmentId && x.WorkCenterId == r.WorkCenterId, ct)) throw new ConflictException("That requirement already exists.");
        db.SkillRequirements.Add(new SkillRequirement { SkillId = skillId, SiteId = r.SiteId, DepartmentId = r.DepartmentId, WorkCenterId = r.WorkCenterId });
        await db.SaveChangesAsync(ct);
    }

    public async Task<EmployeeCertification> CertifyAsync(Guid skillId, CertificationInput r, CancellationToken ct)
    {
        await m.EditAsync(SkillType, skillId, ct);
        var s = await db.Skills.AsNoTracking().FirstAsync(x => x.Id == skillId, ct);
        await m.RequireActiveUserAsync(r.UserId, "certified person", ct);
        if (r.ExpiresOn is { } e && e <= r.CertifiedOn) throw new AppException("The expiry date must be after the certification date.");
        if (r.CertifiedOn > await m.TodayAsync(s.SiteId, ct)) throw new AppException("The certification date cannot be in the future.");
        var c = new EmployeeCertification { UserId = r.UserId, SkillId = skillId, SiteId = s.SiteId, DepartmentId = s.DepartmentId, CertifiedOn = r.CertifiedOn, ExpiresOn = r.ExpiresOn,
            Reference = r.Reference?.Trim(), IssuedBy = r.IssuedBy?.Trim() };
        db.EmployeeCertifications.Add(c);
        await db.SaveChangesAsync(ct);
        return c;
    }

    public async Task RevokeAsync(Guid certificationId, string reason, CancellationToken ct)
    {
        var c = await db.EmployeeCertifications.FirstOrDefaultAsync(x => x.Id == certificationId, ct) ?? throw new NotFoundException("Certification");
        await m.EditAsync(SkillType, c.SkillId, ct);
        ModuleSupport.Required(reason, "Reason", 1000);
        if (c.Status == CertificationStatus.Revoked) throw new ConflictException("Already revoked.");
        c.Status = CertificationStatus.Revoked; c.RevokedReason = reason.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<EmployeeCertification>> CertificationsAsync(Guid? userId, CancellationToken ct) =>
        await (await m.ScopeAsync(db.EmployeeCertifications.AsNoTracking(), Permissions.TrainingView, ct)).Where(c => userId == null || c.UserId == userId).OrderByDescending(c => c.CertifiedOn).ToListAsync(ct);

    /// <summary>Matrix for a site, optionally narrowed to a department or production line. People are the site's active users (by default site or department).</summary>
    public async Task<SkillsMatrix> MatrixAsync(Guid siteId, Guid? departmentId, Guid? workCenterId, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.TrainingView, siteId, departmentId, ct);
        var reqs = await db.SkillRequirements.AsNoTracking().Where(r => r.SiteId == siteId && (departmentId == null || r.DepartmentId == null || r.DepartmentId == departmentId)
            && (workCenterId == null || r.WorkCenterId == null || r.WorkCenterId == workCenterId)).ToListAsync(ct);
        var skillIds = reqs.Select(r => r.SkillId).Distinct().ToList();
        var skills = await db.Skills.AsNoTracking().Where(s => (skillIds.Contains(s.Id) || s.SiteId == siteId) && !s.IsArchived).OrderBy(s => s.Code).ToListAsync(ct);
        var people = await db.Users.AsNoTracking().Where(u => u.IsActive && u.PortalKind == null && (u.DefaultSiteId == siteId || db.UserRoleAssignments.Any(a => a.UserId == u.Id && a.SiteId == siteId))
            && (departmentId == null || u.DepartmentId == departmentId)).OrderBy(u => u.DisplayName).Select(u => new MatrixPerson(u.Id, u.DisplayName, u.DepartmentId)).ToListAsync(ct);
        var results = await q.CheckManyAsync(people.Select(p => p.Id).ToList(), skills, ct);
        var cells = results.Select(kv => new MatrixCell(kv.Key.User, kv.Key.Skill, kv.Value.Status, kv.Value.Qualified, kv.Value.Reason, kv.Value.ExpiresOn)).ToList();
        var requiredBy = reqs.GroupBy(r => r.SkillId).ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());
        return new SkillsMatrix(skills, people, cells, requiredBy);
    }

    /// <summary>Gap report: required skills a person in scope does not currently hold, plus those expiring within 30 days.</summary>
    public async Task<List<QualificationGap>> GapsAsync(Guid siteId, Guid? departmentId, Guid? workCenterId, CancellationToken ct)
    {
        var mx = await MatrixAsync(siteId, departmentId, workCenterId, ct);
        var reqs = await db.SkillRequirements.AsNoTracking().Where(r => r.SiteId == siteId).ToListAsync(ct);
        bool Required(Guid skill, MatrixPerson p) => reqs.Any(r => r.SkillId == skill && (r.DepartmentId == null || r.DepartmentId == p.DepartmentId) && (workCenterId == null || r.WorkCenterId == null || r.WorkCenterId == workCenterId));
        return (from c in mx.Cells
                join p in mx.People on c.UserId equals p.Id
                join s in mx.Skills on c.SkillId equals s.Id
                where Required(s.Id, p) && (!c.Qualified || c.Status == "Expiring")
                orderby p.Name, s.Code
                select new QualificationGap(p.Id, p.Name, s.Id, s.Code + " " + s.Name, c.Status, c.Reason, c.ExpiresOn)).ToList();
    }

    // ---------- curricula ----------
    public async Task<TrainingCurriculum> CreateCurriculumAsync(CurriculumInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.TrainingEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300);
        if (r.CourseIds.Count == 0) throw new AppException("Add at least one course.");
        await ValidateCoursesAsync(r.CourseIds, ct);
        if (r.GrantsSkillId is { } g && !await db.Skills.AnyAsync(s => s.Id == g, ct)) throw new AppException("Select an existing skill.");
        if (r.RefresherLeadDays is < 0 or > 365) throw new AppException("Refresher lead time must be 0–365 days.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new TrainingCurriculum { Number = await numbers.NextAsync(CurriculumType, r.SiteId, ct), Title = r.Title.Trim(), Kind = r.Kind, SiteId = r.SiteId, DepartmentId = r.DepartmentId,
            CourseIds = r.CourseIds, AutoAssign = r.AutoAssign, GrantsSkillId = r.GrantsSkillId, RefresherLeadDays = r.RefresherLeadDays };
        db.TrainingCurricula.Add(c);
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(CurriculumType, c.Id, c.Title, ct);
        await tx.CommitAsync(ct);
        return c;
    }

    public async Task<List<TrainingCurriculum>> CurriculaAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.TrainingCurricula.AsNoTracking(), Permissions.TrainingView, ct)).Where(c => !c.IsArchived).OrderBy(c => c.Number).ToListAsync(ct);

    /// <summary>Assign a curriculum. Training paths release only the first course not yet completed; the next follows on completion.</summary>
    public async Task<int> AssignCurriculumAsync(Guid id, List<Guid> userIds, CancellationToken ct)
    {
        await m.EditAsync(CurriculumType, id, ct);
        var c = await db.TrainingCurricula.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) throw new AppException("Select at least one person.");
        if (await db.Users.CountAsync(u => ids.Contains(u.Id) && u.IsActive, ct) != ids.Count) throw new AppException("Every trainee must be an active user.");
        return await AssignCoreAsync(c, ids, "Initial training", ct);
    }

    async Task<int> AssignCoreAsync(TrainingCurriculum c, List<Guid> userIds, string reason, CancellationToken ct)
    {
        var done = await db.TrainingAssignments.AsNoTracking().Where(a => userIds.Contains(a.UserId) && c.CourseIds.Contains(a.CourseId) && (a.Status == AssignmentStatus.Completed || a.Status == AssignmentStatus.Assigned))
            .Select(a => new { a.UserId, a.CourseId }).ToListAsync(ct);
        var n = 0;
        foreach (var courseId in c.CourseIds)
        {
            var need = userIds.Where(u => !done.Any(d => d.UserId == u && d.CourseId == courseId)).ToList();
            if (c.Kind == CurriculumKind.TrainingPath)
            {
                // Path: only people who have every earlier course in hand (completed or assigned) get this one, and only when the previous one is completed.
                var idx = c.CourseIds.IndexOf(courseId);
                if (idx > 0)
                {
                    var prev = c.CourseIds[idx - 1];
                    var prevDone = await db.TrainingAssignments.AsNoTracking().Where(a => need.Contains(a.UserId) && a.CourseId == prev && a.Status == AssignmentStatus.Completed).Select(a => a.UserId).Distinct().ToListAsync(ct);
                    need = need.Intersect(prevDone).ToList();
                }
            }
            if (need.Count > 0) n += await training.AssignSystemAsync(courseId, need, $"{reason} ({c.Number})", ct);
        }
        return n;
    }

    /// <summary>On completion: release the next course of a path, and issue the certification for a completed certification or qualification program.</summary>
    public async Task OnCompletedAsync(TrainingAssignment a, CancellationToken ct)
    {
        var curricula = await db.TrainingCurricula.AsNoTracking().Where(c => !c.IsArchived && c.CourseIds.Contains(a.CourseId)).ToListAsync(ct);
        foreach (var c in curricula)
        {
            if (c.Kind == CurriculumKind.TrainingPath) await AssignCoreAsync(c, [a.UserId], "Training path", ct);
            if (c.GrantsSkillId is not { } skillId) continue;
            var completed = await db.TrainingAssignments.AsNoTracking().Where(x => x.UserId == a.UserId && c.CourseIds.Contains(x.CourseId) && x.Status == AssignmentStatus.Completed).Select(x => x.CourseId).Distinct().CountAsync(ct);
            if (completed < c.CourseIds.Count) continue;
            var skill = await db.Skills.AsNoTracking().FirstAsync(s => s.Id == skillId, ct);
            if (!skill.RequiresCertification && skill.CourseIds.Count > 0) continue; // held through course completion alone
            var today = await m.TodayAsync(skill.SiteId, ct);
            var expires = await db.TrainingAssignments.AsNoTracking().Where(x => x.UserId == a.UserId && c.CourseIds.Contains(x.CourseId) && x.Status == AssignmentStatus.Completed && x.ExpiresOn != null).MinAsync(x => x.ExpiresOn, ct);
            if (await db.EmployeeCertifications.AnyAsync(x => x.UserId == a.UserId && x.SkillId == skillId && x.Status == CertificationStatus.Active && (x.ExpiresOn == null || x.ExpiresOn >= today) && x.CertifiedOn == today, ct)) continue;
            db.EmployeeCertifications.Add(new EmployeeCertification { UserId = a.UserId, SkillId = skillId, SiteId = skill.SiteId, DepartmentId = skill.DepartmentId, CertifiedOn = today, ExpiresOn = expires,
                Reference = c.Number, IssuedBy = $"{c.Kind} {c.Number}" });
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Nightly (and on demand): auto-assign curricula to everyone in their scope and assign refresher training before qualifications expire.</summary>
    public async Task<(int Assigned, int Refreshers)> AutoAssignAsync(CancellationToken ct)
    {
        var assigned = 0; var refreshers = 0;
        foreach (var c in await db.TrainingCurricula.AsNoTracking().Where(c => !c.IsArchived).ToListAsync(ct))
        {
            if (c.AutoAssign)
            {
                var users = await db.Users.AsNoTracking().Where(u => u.IsActive && u.PortalKind == null && (c.DepartmentId != null ? u.DepartmentId == c.DepartmentId : u.DefaultSiteId == c.SiteId)).Select(u => u.Id).ToListAsync(ct);
                if (users.Count > 0) assigned += await AssignCoreAsync(c, users, "Automatic assignment", ct);
            }
            var today = await m.TodayAsync(c.SiteId, ct);
            var horizon = today.AddDays(c.RefresherLeadDays);
            var expiring = await db.TrainingAssignments.AsNoTracking().Where(a => c.CourseIds.Contains(a.CourseId) && a.Status == AssignmentStatus.Completed && a.ExpiresOn != null && a.ExpiresOn <= horizon
                    && !db.TrainingAssignments.Any(o => o.CourseId == a.CourseId && o.UserId == a.UserId && (o.Status == AssignmentStatus.Assigned || (o.Status == AssignmentStatus.Completed && o.CompletedAt > a.CompletedAt))))
                .Select(a => new { a.CourseId, a.UserId }).Distinct().ToListAsync(ct);
            foreach (var g in expiring.GroupBy(x => x.CourseId))
                refreshers += await training.AssignSystemAsync(g.Key, g.Select(x => x.UserId).Distinct().ToList(), "Refresher training", ct);
        }
        return (assigned, refreshers);
    }
}
