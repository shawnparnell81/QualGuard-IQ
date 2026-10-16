using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

[Collection("api")]
public sealed class DocumentControlTests(TestApp app)
{
    private static readonly string[] Author = [Permissions.DocumentsView, Permissions.DocumentsEdit];
    private static readonly string[] Approver = [Permissions.DocumentsView, Permissions.DocumentsApprove];

    private async Task ReleaseAsync(ApiClient author, ApiClient approver, Guid docId, string content)
    {
        var doc = await author.GetDocumentAsync(docId);
        var draft = doc.Revisions.Single(r => r.Status == 0);
        (await author.UploadAsync("DocumentRevision", draft.Id, $"procedure-{draft.Revision}.pdf", content)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{draft.Id}/submit")).StatusCode);
        var approval = await approver.PendingApprovalIdAsync(doc.Number);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(approval)).StatusCode);
    }

    /// <summary>E2E 1: create, review, approve, release, revise, supersede and retrieve a controlled procedure.</summary>
    [Fact]
    public async Task E2E_controlled_procedure_lifecycle()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(Author, site);
        var (_, reviewer) = await app.CreateUserAsync(Approver, site);
        var (_, approver) = await app.CreateUserAsync(Approver, site);
        var admin = await app.AdminAsync();
        var twoStep = new { name = "Document revision approval", preventSelfApproval = true, steps = new[] {
            new { order = 1, name = "Technical review", requiredPermission = Permissions.DocumentsApprove, signatureMeaning = "Reviewed" },
            new { order = 2, name = "Quality approval", requiredPermission = Permissions.DocumentsApprove, signatureMeaning = "Approved" } } };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsync("/api/admin/config/approval-routes/DocumentRevision", twoStep)).StatusCode);
        try
        {
            var id = await author.CreateDocumentAsync(site);
            var doc = await author.GetDocumentAsync(id);
            Assert.StartsWith("DOC-", doc.Number);
            var revA = Assert.Single(doc.Revisions);

            // cannot submit without the controlled file
            Assert.Equal(HttpStatusCode.BadRequest, (await author.PostAsync($"/api/documents/revisions/{revA.Id}/submit")).StatusCode);
            (await author.UploadAsync("DocumentRevision", revA.Id, "procedure-A.pdf", "%PDF-1.4 rev A")).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{revA.Id}/submit")).StatusCode);

            // author cannot approve (no permission); review step then approval step by different people
            Assert.Empty((await author.GetJsonAsync<MyWork>("/api/my/work")).Approvals);
            var approvalId = await reviewer.PendingApprovalIdAsync(doc.Number);
            Assert.Equal(HttpStatusCode.Unauthorized, (await reviewer.SignAsync(approvalId, password: "wrong-password-123")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await reviewer.SignAsync(approvalId)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.SignAsync(approvalId)).StatusCode); // cannot sign twice
            Assert.Equal(1, (await author.GetDocumentAsync(id)).Revisions.Single().Status); // still in review
            Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(doc.Number))).StatusCode);

            doc = await author.GetDocumentAsync(id);
            Assert.Equal(1, doc.Status); // Active
            revA = doc.Revisions.Single();
            Assert.Equal(3, revA.Status); // Effective
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            Assert.Equal(today, revA.EffectiveDate);

            // the approved revision is immutable and its file is locked
            Assert.Equal(HttpStatusCode.Conflict, (await author.PutAsync($"/api/documents/revisions/{revA.Id}", new { changeSummary = "tamper", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = revA.RowVersion })).StatusCode);
            Assert.NotEqual(HttpStatusCode.OK, (await author.UploadAsync("DocumentRevision", revA.Id, "extra.pdf", "x")).StatusCode);
            var filesA = await author.GetJsonAsync<List<FileDto>>($"/api/records/DocumentRevision/{revA.Id}/files");
            Assert.True(Assert.Single(filesA).IsLocked);

            // signatures recorded
            var approvals = await author.GetJsonAsync<List<ApprovalHistory>>($"/api/records/DocumentRevision/{revA.Id}/approvals");
            Assert.Equal(["Reviewed", "Approved"], approvals.Single().Decisions.Select(d => d.Meaning));

            // revise to B; A stays effective until B is released
            var revBId = await (await author.PostOkAsync($"/api/documents/{id}/revisions", new { revision = (string?)null, changeSummary = "Clarified sampling plan" })).Content.ReadFromJsonAsync<Guid>();
            Assert.Equal(HttpStatusCode.Conflict, (await author.PostAsync($"/api/documents/{id}/revisions", new { changeSummary = "second draft" })).StatusCode);
            doc = await author.GetDocumentAsync(id);
            Assert.Equal("B", doc.Revisions.Single(r => r.Id == revBId).Revision);
            Assert.Equal(3, doc.Revisions.Single(r => r.Id == revA.Id).Status);

            (await author.UploadAsync("DocumentRevision", revBId, "procedure-B.pdf", "%PDF-1.4 rev B")).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{revBId}/submit")).StatusCode);
            await reviewer.SignAsync(await reviewer.PendingApprovalIdAsync(doc.Number));
            await approver.SignAsync(await approver.PendingApprovalIdAsync(doc.Number));

            doc = await author.GetDocumentAsync(id);
            var a = doc.Revisions.Single(r => r.Id == revA.Id);
            var b = doc.Revisions.Single(r => r.Id == revBId);
            Assert.Equal(4, a.Status); // Superseded
            Assert.Equal(today, a.SupersededOn);
            Assert.Equal(3, b.Status);
            Assert.Equal("Initial release", a.ChangeSummary); // original revision unchanged

            // retrieve: current effective, and A's file is still retrievable with its original checksum
            var eff = await author.GetJsonAsync<RevDto>($"/api/documents/{id}/effective");
            Assert.Equal("B", eff.Revision);
            var file = filesA.Single();
            var bytes = await author.Http.GetByteArrayAsync($"/api/files/{file.Id}");
            Assert.Equal(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            Assert.Equal("%PDF-1.4 rev A", Encoding.UTF8.GetString(bytes));
            Assert.Equal(HttpStatusCode.NotFound, (await author.GetAsync($"/api/documents/{id}/effective?on={today.AddDays(-1):yyyy-MM-dd}")).StatusCode);

            // found in search
            var hits = await author.GetJsonAsync<List<SearchHitDto>>($"/api/search?q={doc.Number}");
            Assert.Contains(hits, h => h.RecordId == id);

            // audit history exists for the document
            var history = await author.GetJsonAsync<List<HistoryDto>>($"/api/records/DocumentRevision/{revA.Id}/history");
            Assert.Contains(history, h => h.Action == "Signed");
        }
        finally
        {
            var oneStep = new { name = "Document revision approval", preventSelfApproval = true, steps = new[] {
                new { order = 1, name = "Approval", requiredPermission = Permissions.DocumentsApprove, signatureMeaning = "Approved" } } };
            await admin.PutAsync("/api/admin/config/approval-routes/DocumentRevision", oneStep);
        }
    }

    /// <summary>E2E 2: upload and approve a drawing, then create a controlled revision without altering the original.</summary>
    [Fact]
    public async Task E2E_drawing_revision_preserves_original()
    {
        var site = await app.CreateSiteAsync();
        var (_, engineer) = await app.CreateUserAsync([Permissions.DrawingsView, Permissions.DrawingsEdit], site);
        var (_, checker) = await app.CreateUserAsync([Permissions.DrawingsView, Permissions.DrawingsApprove], site);
        var id = await engineer.CreateDocumentAsync(site, kind: 1, title: "Bracket, mounting", partNumber: "BR-1001");
        var dwg = await engineer.GetDocumentAsync(id);
        Assert.StartsWith("DWG-", dwg.Number);
        var revA = dwg.Revisions.Single();
        (await engineer.UploadAsync("DrawingRevision", revA.Id, "BR-1001-A.pdf", "%PDF drawing A")).EnsureSuccessStatusCode();
        await engineer.PostOkAsync($"/api/documents/revisions/{revA.Id}/submit");
        await checker.SignAsync(await checker.PendingApprovalIdAsync(dwg.Number));
        var fileA = (await engineer.GetJsonAsync<List<FileDto>>($"/api/records/DrawingRevision/{revA.Id}/files")).Single();

        var revB = await (await engineer.PostOkAsync($"/api/documents/{id}/revisions", new { changeSummary = "Hole moved 2 mm" })).Content.ReadFromJsonAsync<Guid>();
        // the original file cannot be replaced under revision A
        Assert.Equal(HttpStatusCode.BadRequest, (await engineer.UploadAsync("DrawingRevision", revA.Id, "BR-1001-A.pdf", "changed", fileA.Id)).StatusCode);
        (await engineer.UploadAsync("DrawingRevision", revB, "BR-1001-B.pdf", "%PDF drawing B")).EnsureSuccessStatusCode();
        await engineer.PostOkAsync($"/api/documents/revisions/{revB}/submit");
        await checker.SignAsync(await checker.PendingApprovalIdAsync(dwg.Number));

        var after = (await engineer.GetJsonAsync<List<FileDto>>($"/api/records/DrawingRevision/{revA.Id}/files")).Single();
        Assert.Equal(fileA.Sha256, after.Sha256);
        Assert.Equal("%PDF drawing A", await engineer.Http.GetStringAsync($"/api/files/{fileA.Id}"));
        dwg = await engineer.GetDocumentAsync(id);
        Assert.Equal(4, dwg.Revisions.Single(r => r.Id == revA.Id).Status);
        Assert.Equal(3, dwg.Revisions.Single(r => r.Id == revB).Status);
    }

    [Fact]
    public async Task Author_cannot_approve_own_submission_even_with_approve_permission()
    {
        var site = await app.CreateSiteAsync();
        var (_, both) = await app.CreateUserAsync([.. Author, Permissions.DocumentsApprove], site);
        var id = await both.CreateDocumentAsync(site);
        var doc = await both.GetDocumentAsync(id);
        var rev = doc.Revisions.Single();
        (await both.UploadAsync("DocumentRevision", rev.Id, "p.pdf", "x")).EnsureSuccessStatusCode();
        await both.PostOkAsync($"/api/documents/revisions/{rev.Id}/submit");
        Assert.Empty((await both.GetJsonAsync<MyWork>("/api/my/work")).Approvals);
    }

    [Fact]
    public async Task Rejection_requires_reason_and_returns_revision_to_draft()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync(Author, site);
        var (_, approver) = await app.CreateUserAsync(Approver, site);
        var id = await author.CreateDocumentAsync(site);
        var doc = await author.GetDocumentAsync(id);
        var rev = doc.Revisions.Single();
        (await author.UploadAsync("DocumentRevision", rev.Id, "p.pdf", "x")).EnsureSuccessStatusCode();
        await author.PostOkAsync($"/api/documents/revisions/{rev.Id}/submit");
        var aid = await approver.PendingApprovalIdAsync(doc.Number);
        Assert.Equal(HttpStatusCode.BadRequest, (await approver.SignAsync(aid, approve: false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(aid, approve: false, comment: "Section 4 unclear")).StatusCode);
        Assert.Equal(0, (await author.GetDocumentAsync(id)).Revisions.Single().Status);
        var notes = await author.GetJsonAsync<NotificationsDto>("/api/my/notifications");
        Assert.Contains(notes.Items, n => n.Title.Contains("rejected") && n.Link!.Contains(id.ToString()));
    }

    [Fact]
    public async Task Users_without_site_access_cannot_see_or_edit_documents()
    {
        var siteA = await app.CreateSiteAsync();
        var siteB = await app.CreateSiteAsync();
        var (_, authorA) = await app.CreateUserAsync(Author, siteA);
        var (_, viewerB) = await app.CreateUserAsync([Permissions.DocumentsView, Permissions.DocumentsEdit], siteB);
        var (_, viewOnlyA) = await app.CreateUserAsync([Permissions.DocumentsView], siteA);
        var id = await authorA.CreateDocumentAsync(siteA, title: "Site A only procedure");
        var doc = await authorA.GetDocumentAsync(id);

        Assert.Equal(HttpStatusCode.NotFound, (await viewerB.GetAsync($"/api/documents/{id}")).StatusCode);
        Assert.DoesNotContain((await viewerB.GetJsonAsync<PageDto<DocListDto>>("/api/documents?kind=0&pageSize=200")).Items, d => d.Id == id);
        Assert.Empty(await viewerB.GetJsonAsync<List<SearchHitDto>>($"/api/search?q={doc.Number}"));
        Assert.Equal(HttpStatusCode.NotFound, (await viewerB.GetAsync($"/api/records/Document/{id}/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerB.PostAsync("/api/documents", new { kind = 0, title = "x", documentType = "Procedure", siteId = siteA, reviewIntervalMonths = 12 })).StatusCode);

        // view-only user at the site can read but not change
        Assert.Equal(HttpStatusCode.OK, (await viewOnlyA.GetAsync($"/api/documents/{id}")).StatusCode);
        Assert.False((await viewOnlyA.GetDocumentAsync(id)).CanEdit);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnlyA.UploadAsync("DocumentRevision", doc.Revisions[0].Id, "x.pdf", "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnlyA.PutAsync($"/api/documents/{id}", new { title = "Changed", documentType = "Procedure", ownerId = Guid.NewGuid(), reviewIntervalMonths = 12, rowVersion = doc.RowVersion })).StatusCode);
        // but may comment
        Assert.Equal(HttpStatusCode.OK, (await viewOnlyA.PostAsync($"/api/records/Document/{id}/comments", new { body = "Please add a figure." })).StatusCode);
    }

    [Fact]
    public void Revision_labels_increment()
    {
        Assert.Equal("B", QualGuardIQ.Infrastructure.Modules.Documents.DocumentService.NextLabel("A"));
        Assert.Equal("AA", QualGuardIQ.Infrastructure.Modules.Documents.DocumentService.NextLabel("Z"));
        Assert.Equal("AB", QualGuardIQ.Infrastructure.Modules.Documents.DocumentService.NextLabel("AA"));
        Assert.Equal("02", QualGuardIQ.Infrastructure.Modules.Documents.DocumentService.NextLabel("01"));
        Assert.Equal("10", QualGuardIQ.Infrastructure.Modules.Documents.DocumentService.NextLabel("9"));
    }
}

public sealed record DecisionHistory(string StepName, string SignerName, string Meaning);
public sealed record ApprovalHistory(Guid Id, int Status, List<DecisionHistory> Decisions);
public sealed record SearchHitDto(string RecordType, Guid RecordId, string Number, string Title);
public sealed record HistoryDto(DateTimeOffset OccurredAt, string UserName, string Action, string Description);
public sealed record NotificationItem(long Id, string Title, string? Link, DateTimeOffset? ReadAt);
public sealed record NotificationsDto(int Unread, List<NotificationItem> Items);
public sealed record DocListDto(Guid Id, string Number, string Title);
