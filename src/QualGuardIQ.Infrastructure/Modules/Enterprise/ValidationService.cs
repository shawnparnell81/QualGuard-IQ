using QualGuardIQ.Application.Records;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;
using QualGuardIQ.Infrastructure.Services;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

public sealed record ValidationInput(string Title, ValidationKind Kind, Guid SiteId, Guid? DepartmentId, string Scope, Guid? EquipmentId, Guid? PartId, Guid? OwnerId);
public sealed record ValidationTestInput(string Stage, int Sequence, string Description, string AcceptanceCriteria);
public sealed record ExecuteTestInput(string ActualResult, bool Passed);
public sealed record DeviationInput(Guid? TestId, string Description, string? Impact);
public sealed record ResolveDeviationInput(string Resolution);

/// <summary>
/// Validation: Planning → protocol approval (e-signed) → execution (test results, deviations) → results approval (e-signed) → Released.
/// Both approvals use the shared approval engine under the "Validation" record type; the handler moves the project by its current status.
/// </summary>
public sealed class ValidationService(AppDbContext db, ModuleSupport m, INumberingService numbers, WorkflowService workflow, INotifier notifier)
{
    public const string RecordType = "Validation";

    async Task<ValidationProject> LoadEditAsync(Guid id, CancellationToken ct)
    {
        await m.EditAsync(RecordType, id, ct);
        return await db.ValidationProjects.FirstAsync(x => x.Id == id, ct);
    }

    public async Task<ValidationProject> CreateAsync(ValidationInput r, CancellationToken ct)
    {
        await m.RequireAsync(Permissions.ValidationEdit, r.SiteId, r.DepartmentId, ct);
        await m.ValidateLocationAsync(r.SiteId, r.DepartmentId, ct);
        ModuleSupport.Required(r.Title, "Title", 300); ModuleSupport.Required(r.Scope, "Scope", 4000);
        if (r.Kind == ValidationKind.Equipment && r.EquipmentId is null) throw new AppException("An equipment validation needs the equipment.");
        if (r.EquipmentId is { } e && !await db.Equipment.AnyAsync(x => x.Id == e, ct)) throw new AppException("Select existing equipment.");
        if (r.PartId is { } p && !await db.Parts.AnyAsync(x => x.Id == p, ct)) throw new AppException("Select an existing part.");
        if (r.OwnerId is { } o) await m.RequireActiveUserAsync(o, "owner", ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var v = new ValidationProject { Number = await numbers.NextAsync(RecordType, r.SiteId, ct), Title = r.Title.Trim(), Kind = r.Kind, SiteId = r.SiteId, DepartmentId = r.DepartmentId, Scope = r.Scope.Trim(),
            EquipmentId = r.EquipmentId, PartId = r.PartId, OwnerId = r.OwnerId ?? m.UserId, Status = ValidationStatus.Planning };
        db.ValidationProjects.Add(v);
        if (r.EquipmentId is { } eq) db.RecordLinks.Add(new RecordLink { FromType = RecordType, FromId = v.Id, ToType = "Equipment", ToId = eq, LinkType = "Validates" });
        await db.SaveChangesAsync(ct);
        await m.IndexAsync(RecordType, v.Id, v.Scope, ct);
        await tx.CommitAsync(ct);
        return v;
    }

    public async Task<List<ValidationProject>> ListAsync(CancellationToken ct) =>
        await (await m.ScopeAsync(db.ValidationProjects.AsNoTracking(), Permissions.ValidationView, ct)).OrderByDescending(v => v.CreatedAt).Take(500).ToListAsync(ct);

    public async Task<object> GetAsync(Guid id, CancellationToken ct)
    {
        await m.ViewAsync(RecordType, id, ct);
        var v = await db.ValidationProjects.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        var tests = await db.ValidationTests.AsNoTracking().Where(t => t.ProjectId == id).OrderBy(t => t.Stage).ThenBy(t => t.Sequence).ToListAsync(ct);
        var devs = await db.ValidationDeviations.AsNoTracking().Where(d => d.ProjectId == id).OrderBy(d => d.CreatedAt).ToListAsync(ct);
        var approvals = await db.ApprovalRequests.AsNoTracking().Include(a => a.Decisions).Where(a => a.RecordType == RecordType && a.RecordId == id).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        return new { Project = v, Status = v.Status.ToString(), Tests = tests, Deviations = devs, Approvals = approvals,
            Summary = new { Total = tests.Count, Executed = tests.Count(t => t.Passed != null), Passed = tests.Count(t => t.Passed == true), Failed = tests.Count(t => t.Passed == false), OpenDeviations = devs.Count(d => d.Status == DeviationStatus.Open) } };
    }

    public async Task SaveTestsAsync(Guid id, List<ValidationTestInput> tests, CancellationToken ct)
    {
        var v = await LoadEditAsync(id, ct);
        if (v.Status != ValidationStatus.Planning) throw new ConflictException("The protocol is locked once submitted; record a deviation instead.");
        if (tests.Count == 0) throw new AppException("A protocol needs at least one test.");
        if (tests.Select(t => (t.Stage.Trim().ToUpperInvariant(), t.Sequence)).Distinct().Count() != tests.Count) throw new AppException("Each stage and sequence must be unique.");
        foreach (var t in tests) { ModuleSupport.Required(t.Stage, "Stage", 20); ModuleSupport.Required(t.Description, "Test description", 2000); ModuleSupport.Required(t.AcceptanceCriteria, "Acceptance criteria", 2000); }
        db.ValidationTests.RemoveRange(await db.ValidationTests.Where(t => t.ProjectId == id).ToListAsync(ct));
        db.ValidationTests.AddRange(tests.Select(t => new ValidationTest { ProjectId = id, Stage = t.Stage.Trim().ToUpperInvariant(), Sequence = t.Sequence, Description = t.Description.Trim(), AcceptanceCriteria = t.AcceptanceCriteria.Trim() }));
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitProtocolAsync(Guid id, CancellationToken ct)
    {
        var v = await LoadEditAsync(id, ct);
        if (v.Status != ValidationStatus.Planning) throw new ConflictException($"The validation is {v.Status}.");
        if (!await db.ValidationTests.AnyAsync(t => t.ProjectId == id, ct)) throw new AppException("Add the protocol tests before submitting.");
        await workflow.SubmitAsync(RecordType, id, ct);
        v.Status = ValidationStatus.ProtocolApproval;
        await db.SaveChangesAsync(ct);
    }

    public async Task ExecuteAsync(Guid id, Guid testId, ExecuteTestInput r, CancellationToken ct)
    {
        var v = await LoadEditAsync(id, ct);
        if (v.Status != ValidationStatus.Execution) throw new ConflictException("Tests are executed after the protocol is approved.");
        var t = await db.ValidationTests.FirstOrDefaultAsync(x => x.Id == testId && x.ProjectId == id, ct) ?? throw new NotFoundException("Test");
        ModuleSupport.Required(r.ActualResult, "Actual result", 4000);
        if (t.Passed is not null) throw new ConflictException("This test already has a recorded result; record a deviation to re-run it.");
        t.ActualResult = r.ActualResult.Trim(); t.Passed = r.Passed; t.ExecutedById = m.UserId; t.ExecutedAt = m.Now;
        if (!r.Passed) db.ValidationDeviations.Add(new ValidationDeviation { ProjectId = id, TestId = testId, Description = $"{t.Stage}-{t.Sequence} did not meet: {t.AcceptanceCriteria}. Result: {t.ActualResult}" });
        await db.SaveChangesAsync(ct);
    }

    public async Task<ValidationDeviation> AddDeviationAsync(Guid id, DeviationInput r, CancellationToken ct)
    {
        var v = await LoadEditAsync(id, ct);
        if (v.Status is ValidationStatus.Released or ValidationStatus.Cancelled) throw new ConflictException($"The validation is {v.Status}.");
        ModuleSupport.Required(r.Description, "Description", 4000);
        if (r.TestId is { } tid && !await db.ValidationTests.AnyAsync(t => t.Id == tid && t.ProjectId == id, ct)) throw new AppException("Select a test of this protocol.");
        var d = new ValidationDeviation { ProjectId = id, TestId = r.TestId, Description = r.Description.Trim(), Impact = r.Impact?.Trim() };
        db.ValidationDeviations.Add(d);
        await db.SaveChangesAsync(ct);
        return d;
    }

    /// <summary>Resolve a deviation. When it came from a failed test, the test is cleared so it can be re-executed.</summary>
    public async Task ResolveDeviationAsync(Guid id, Guid deviationId, ResolveDeviationInput r, bool retest, CancellationToken ct)
    {
        var v = await LoadEditAsync(id, ct);
        var d = await db.ValidationDeviations.FirstOrDefaultAsync(x => x.Id == deviationId && x.ProjectId == id, ct) ?? throw new NotFoundException("Deviation");
        if (d.Status == DeviationStatus.Resolved) throw new ConflictException("Already resolved.");
        ModuleSupport.Required(r.Resolution, "Resolution", 4000);
        d.Resolution = r.Resolution.Trim(); d.Status = DeviationStatus.Resolved;
        if (retest && d.TestId is { } tid && v.Status == ValidationStatus.Execution)
        {
            var t = await db.ValidationTests.FirstAsync(x => x.Id == tid, ct);
            t.Passed = null; t.ActualResult = null; t.ExecutedAt = null; t.ExecutedById = null;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task SubmitResultsAsync(Guid id, string conclusion, CancellationToken ct)
    {
        var v = await LoadEditAsync(id, ct);
        if (v.Status != ValidationStatus.Execution) throw new ConflictException($"The validation is {v.Status}.");
        ModuleSupport.Required(conclusion, "Conclusion", 4000);
        var tests = await db.ValidationTests.AsNoTracking().Where(t => t.ProjectId == id).ToListAsync(ct);
        if (tests.Any(t => t.Passed is null)) throw new AppException($"{tests.Count(t => t.Passed is null)} test(s) have no result yet.");
        if (await db.ValidationDeviations.AnyAsync(d => d.ProjectId == id && d.Status == DeviationStatus.Open, ct)) throw new AppException("Resolve every open deviation first.");
        if (tests.Any(t => t.Passed == false)) throw new AppException("A failed test must be resolved and re-executed before results can be approved.");
        await workflow.SubmitAsync(RecordType, id, ct);
        v.Conclusion = conclusion.Trim(); v.Status = ValidationStatus.ResultsApproval;
        await db.SaveChangesAsync(ct);
    }

    public async Task OnApprovedAsync(Guid id, CancellationToken ct)
    {
        var v = await db.ValidationProjects.FirstAsync(x => x.Id == id, ct);
        if (v.Status == ValidationStatus.ProtocolApproval) v.Status = ValidationStatus.Execution;
        else if (v.Status == ValidationStatus.ResultsApproval) { v.Status = ValidationStatus.Released; v.ReleasedAt = m.Now; }
        await db.SaveChangesAsync(ct);
        await notifier.NotifyAsync([v.OwnerId], $"{v.Number} is now {v.Status}", v.Title, await m.RecordAsync(RecordType, id, ct), ct: ct);
    }

    public async Task OnRejectedAsync(Guid id, string? comment, CancellationToken ct)
    {
        var v = await db.ValidationProjects.FirstAsync(x => x.Id == id, ct);
        v.Status = v.Status == ValidationStatus.ProtocolApproval ? ValidationStatus.Planning : ValidationStatus.Execution;
        await db.SaveChangesAsync(ct);
    }
}
