using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Modules.Quality;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Infrastructure.Modules.Enterprise;

public sealed record MappingSubmitInput(string SourceTitle, string Edition, string Publisher, string Reference, string? Comment);
public sealed record MappingDecisionInput(bool Verify, string Comment, DateOnly? EffectiveOn);
public sealed record MappingStatus(string Key, string Name, int MappingVersion, MappingVerificationState State, bool Verified, string Label, string Disclaimer);

/// <summary>
/// Verification workflow for shipped compliance mapping content: Draft/Unverified → Under review → Verified or Rejected; a verified
/// version is superseded when a newer version is verified or when the shipped content changes. Every change is kept as an event.
/// Verification needs compliance.verify and a different person from the one who submitted it. No standard text is stored.
/// </summary>
public sealed class ComplianceMappingService(AppDbContext db, ModuleSupport m)
{
    public const string PpapKey = "ppap-matrix";
    public const string Disclaimer = "Mapping content is an aid for organizing evidence. It is not a statement or guarantee that the organization complies with any standard; compliance is established by audit against the authoritative edition.";

    static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    static string PackContent(CompliancePack p) => JsonSerializer.Serialize(p.Clauses.Select(c => new[] { c.Ref, c.Title }));
    static string PpapContent() => JsonSerializer.Serialize(PpapMatrix.Rows.Select(r => new[] { r.Key, r.Name, r.Condition ?? "", r.Codes }));

    static ComplianceMapping NewFor(string key, ComplianceMappingKind kind, string name, string hash, int version) =>
        new() { Key = key, Kind = kind, Name = name, MappingVersion = version, ContentHash = hash, PackVersion = hash[..12], State = MappingVerificationState.Unverified };

    /// <summary>Creates the verification records for shipped content (idempotent). Changed content starts a new Unverified version and supersedes the old one.</summary>
    public static async Task<int> EnsureSeededAsync(AppDbContext db, DateOnly today, CancellationToken ct = default)
    {
        var shipped = ComplianceService.Packs.Select(p => (Key: "pack:" + p.Code, Kind: ComplianceMappingKind.CompliancePack, p.Name, Hash: Hash(PackContent(p))))
            .Append((Key: PpapKey, Kind: ComplianceMappingKind.PpapMatrix, Name: "PPAP submission level matrix (AIAG PPAP 4th edition, Table 4.2)", Hash: Hash(PpapContent()))).ToList();
        var existing = await db.ComplianceMappings.ToListAsync(ct);
        var added = 0;
        foreach (var s in shipped)
        {
            var versions = existing.Where(x => x.Key == s.Key).OrderBy(x => x.MappingVersion).ToList();
            if (versions.Any(x => x.ContentHash == s.Hash)) continue;
            foreach (var old in versions.Where(x => x.State is not (MappingVerificationState.Superseded or MappingVerificationState.Rejected)))
            {
                db.ComplianceMappingEvents.Add(new ComplianceMappingEvent { MappingId = old.Id, FromState = old.State, ToState = MappingVerificationState.Superseded, ByName = "System (shipped content changed)", At = DateTimeOffset.UtcNow, Comment = "The shipped content changed; the new version needs verification." });
                old.State = MappingVerificationState.Superseded; old.SupersededOn = today;
            }
            var row = NewFor(s.Key, s.Kind, s.Name, s.Hash, (versions.LastOrDefault()?.MappingVersion ?? 0) + 1);
            if (s.Kind == ComplianceMappingKind.PpapMatrix && versions.Count == 0)
            {
                row.State = MappingVerificationState.Verified;
                row.SourceTitle = "Production Part Approval Process (PPAP), Section 4, Table 4.2 \"Retention/Submission Requirements\"";
                row.Edition = "4th edition";
                row.Publisher = "AIAG — checked against reproductions published by Quality-One International and Accurate Products";
                row.Reference = "https://quality-one.com/wp-content/uploads/2016/12/PPAP-Submission-Levels.pdf ; https://www.accurate-prod.com/wp-content/uploads/2024/04/ppap-level-requirements.pdf";
                row.VerifiedOn = new DateOnly(2026, 10, 17); row.EffectiveOn = row.VerifiedOn;
                row.VerifiedByName = "Cell-by-cell comparison with two independent published reproductions (automated test PpapMatrixTests)";
                row.ReviewComments = "All 20 rows × 5 levels agree with both reproductions. Verified against secondary reproductions, not a licensed copy of the AIAG manual; re-verify if the customer supplies the manual or customer-specific requirements.";
            }
            else
                row.ReviewComments = "Clause numbers and short titles were compiled without access to the licensed standard text. Not verified against an authoritative edition.";
            db.ComplianceMappings.Add(row);
            db.ComplianceMappingEvents.Add(new ComplianceMappingEvent { MappingId = row.Id, ToState = row.State, ByName = "System (shipped content)", At = DateTimeOffset.UtcNow, Comment = row.ReviewComments });
            added++;
        }
        await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>The version that currently counts for a key: the latest verified one that is not superseded, otherwise the latest.</summary>
    public static MappingStatus StatusOf(IEnumerable<ComplianceMapping> versions, string key, string name)
    {
        var list = versions.Where(x => x.Key == key).OrderByDescending(x => x.MappingVersion).ToList();
        var current = list.FirstOrDefault(x => x.State == MappingVerificationState.Verified) ?? list.FirstOrDefault();
        if (current is null) return new(key, name, 0, MappingVerificationState.Unverified, false, "Unverified", Disclaimer);
        var verified = current.State == MappingVerificationState.Verified;
        var label = verified ? $"Verified against {current.SourceTitle} ({current.Edition}) on {current.VerifiedOn:yyyy-MM-dd}" : $"{current.State} — not verified against an authoritative source";
        return new(key, current.Name, current.MappingVersion, current.State, verified, label, Disclaimer);
    }

    public async Task<Dictionary<string, MappingStatus>> StatusesAsync(CancellationToken ct)
    {
        var all = await db.ComplianceMappings.AsNoTracking().ToListAsync(ct);
        return all.Select(x => x.Key).Distinct().ToDictionary(k => k, k => StatusOf(all, k, k));
    }

    public async Task<object> ListAsync(CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.ComplianceView, ct)) throw new ForbiddenException();
        var all = await db.ComplianceMappings.AsNoTracking().OrderBy(x => x.Kind).ThenBy(x => x.Key).ThenByDescending(x => x.MappingVersion).ToListAsync(ct);
        return new { Disclaimer, Mappings = all.GroupBy(x => x.Key).Select(g => new { Key = g.Key, Status = StatusOf(all, g.Key, g.First().Name), Versions = g.ToList() }) };
    }

    public async Task<List<ComplianceMappingEvent>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.ComplianceView, ct)) throw new ForbiddenException();
        return await db.ComplianceMappingEvents.AsNoTracking().Where(x => x.MappingId == id).OrderBy(x => x.At).ToListAsync(ct);
    }

    async Task<string> NameAsync(CancellationToken ct) => await db.Users.Where(u => u.Id == m.UserId).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "unknown";

    void Event(ComplianceMapping x, MappingVerificationState from, string by, string? comment) =>
        db.ComplianceMappingEvents.Add(new ComplianceMappingEvent { MappingId = x.Id, FromState = from, ToState = x.State, ById = m.UserId, ByName = by, At = m.Now, Comment = comment });

    /// <summary>Records the authoritative source the content was checked against and sends it for review.</summary>
    public async Task SubmitAsync(Guid id, MappingSubmitInput r, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.ComplianceEdit, ct)) throw new ForbiddenException();
        var x = await db.ComplianceMappings.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("Compliance mapping");
        if (x.State is not (MappingVerificationState.Draft or MappingVerificationState.Unverified or MappingVerificationState.Rejected))
            throw new AppException($"A {x.State} mapping cannot be submitted for review.");
        ModuleSupport.Required(r.SourceTitle, "Source title", 300); ModuleSupport.Required(r.Edition, "Edition", 100);
        ModuleSupport.Required(r.Publisher, "Owner / publisher", 300); ModuleSupport.Required(r.Reference, "Reference", 1000);
        var from = x.State;
        x.SourceTitle = r.SourceTitle.Trim(); x.Edition = r.Edition.Trim(); x.Publisher = r.Publisher.Trim(); x.Reference = r.Reference.Trim();
        x.State = MappingVerificationState.UnderReview; x.SubmittedById = m.UserId;
        Event(x, from, await NameAsync(ct), r.Comment?.Trim());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Verifies or rejects a mapping under review (compliance.verify; not the submitter). A verified version supersedes earlier verified ones.</summary>
    public async Task DecideAsync(Guid id, MappingDecisionInput r, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.ComplianceVerify, ct)) throw new ForbiddenException();
        var x = await db.ComplianceMappings.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("Compliance mapping");
        if (x.State != MappingVerificationState.UnderReview) throw new AppException("Only a mapping under review can be verified or rejected.");
        if (x.SubmittedById == m.UserId) throw new ForbiddenException("The person who submitted a mapping cannot verify it.");
        ModuleSupport.Required(r.Comment, "Review comments", 4000);
        var by = await NameAsync(ct);
        var today = DateOnly.FromDateTime(m.Now.UtcDateTime);
        if (r.Verify)
        {
            var effective = r.EffectiveOn ?? today;
            foreach (var old in await db.ComplianceMappings.Where(v => v.Key == x.Key && v.Id != x.Id && v.State == MappingVerificationState.Verified).ToListAsync(ct))
            {
                old.State = MappingVerificationState.Superseded; old.SupersededOn = effective;
                Event(old, MappingVerificationState.Verified, by, $"Superseded by version {x.MappingVersion}.");
            }
            x.State = MappingVerificationState.Verified; x.VerifiedOn = today; x.VerifiedById = m.UserId; x.VerifiedByName = by; x.EffectiveOn = effective; x.SupersededOn = null;
        }
        else x.State = MappingVerificationState.Rejected;
        x.ReviewComments = r.Comment.Trim();
        Event(x, MappingVerificationState.UnderReview, by, r.Comment.Trim());
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Starts a replacement version (Draft) of the same content, e.g. to verify against a newer edition. Earlier versions stay as history.</summary>
    public async Task<ComplianceMapping> ReviseAsync(Guid id, CancellationToken ct)
    {
        if (!await m.CanAnyAsync(Permissions.ComplianceEdit, ct)) throw new ForbiddenException();
        var x = await db.ComplianceMappings.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException("Compliance mapping");
        if (await db.ComplianceMappings.AnyAsync(v => v.Key == x.Key && (v.State == MappingVerificationState.Draft || v.State == MappingVerificationState.UnderReview), ct))
            throw new ConflictException("A draft or review is already open for this mapping.");
        var next = await db.ComplianceMappings.Where(v => v.Key == x.Key).MaxAsync(v => v.MappingVersion, ct) + 1;
        var row = new ComplianceMapping { Key = x.Key, Kind = x.Kind, Name = x.Name, MappingVersion = next, ContentHash = x.ContentHash, PackVersion = x.PackVersion, State = MappingVerificationState.Draft,
            SourceTitle = x.SourceTitle, Edition = x.Edition, Publisher = x.Publisher, Reference = x.Reference };
        db.ComplianceMappings.Add(row);
        db.ComplianceMappingEvents.Add(new ComplianceMappingEvent { MappingId = row.Id, ToState = row.State, ById = m.UserId, ByName = await NameAsync(ct), At = m.Now, Comment = $"Replacement of version {x.MappingVersion}." });
        await db.SaveChangesAsync(ct);
        return row;
    }
}
