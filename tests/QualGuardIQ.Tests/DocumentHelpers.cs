using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace QualGuardIQ.Tests;

public static class DocumentHelpers
{
    public static async Task<Guid> CreateDocumentAsync(this ApiClient c, Guid siteId, int kind = 0, string title = "Receiving inspection procedure", string? partNumber = null)
    {
        var r = await c.PostOkAsync("/api/documents", new
        {
            kind, title, documentType = kind switch { 0 => "Procedure", 1 => "Detail Drawing", _ => "Inspection Record" }, siteId, departmentId = (Guid?)null, ownerId = (Guid?)null,
            reviewIntervalMonths = 24, initialRevision = "A", partNumber, drawingSize = kind == 1 ? "B" : null, sheetCount = kind == 1 ? 2 : (int?)null, changeSummary = "Initial release"
        });
        return await r.Content.ReadFromJsonAsync<Guid>();
    }

    public static async Task<DocDetail> GetDocumentAsync(this ApiClient c, Guid id) => await c.GetJsonAsync<DocDetail>($"/api/documents/{id}");

    public static async Task<HttpResponseMessage> UploadAsync(this ApiClient c, string type, Guid id, string fileName, string content, Guid? replacesId = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".pdf") ? "application/pdf" : "text/plain");
        form.Add(file, "file", fileName);
        form.Add(new StringContent("0"), "classification");
        if (replacesId is not null) form.Add(new StringContent(replacesId.ToString()!), "replacesId");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/records/{type}/{id}/files") { Content = form };
        return await c.SendWithCsrfAsync(req);
    }

    public static async Task<Guid> PendingApprovalIdAsync(this ApiClient approver, string recordNumberPrefix)
    {
        var work = await approver.GetJsonAsync<MyWork>("/api/my/work");
        return work.Approvals.Single(a => a.RecordNumber.StartsWith(recordNumberPrefix)).Id;
    }

    public static async Task<HttpResponseMessage> SignAsync(this ApiClient approver, Guid approvalId, bool approve = true, string password = "long-enough-password", string? comment = null) =>
        await approver.PostAsync($"/api/my/approvals/{approvalId}/sign", new { approve, password, comment });
}

public sealed record RevDto(Guid Id, string Revision, int Sequence, int Status, string ChangeSummary, DateOnly? EffectiveDate, DateOnly? SupersededOn, bool TrainingRequired, uint RowVersion);
public sealed record DocDetail(Guid Id, int Kind, string Number, string Title, int Status, Guid SiteId, List<RevDto> Revisions, bool CanEdit, bool CanApprove, uint RowVersion);
public sealed record ApprovalItem(Guid Id, string RecordType, Guid RecordId, string RecordNumber, string StepName);
public sealed record MyWork(List<ApprovalItem> Approvals, List<ActionItemDto> Assigned, List<ActionItemDto> Overdue);
public sealed record ActionItemDto(Guid Id, string Number, string Title, Guid AssigneeId, DateOnly DueDate, int Status, bool IsOverdue);
public sealed record FileDto(Guid Id, string FileName, long SizeBytes, string Sha256, int Version, Guid LineageId, bool IsLocked, bool IsArchived);
