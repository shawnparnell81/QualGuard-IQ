using System.Net;
using QualGuardIQ.Application.Records;
using QualGuardIQ.Domain.Security;

namespace QualGuardIQ.Tests;

/// <summary>Gap 20: friendly record type labels are display metadata; identifiers in the API and database are unchanged.</summary>
[Collection("api")]
public sealed class GapClosureLabelTests(TestApp app)
{
    [Theory]
    [InlineData("ChangeRequest", "Change Request")]
    [InlineData("Fai", "First Article Inspection")]
    [InlineData("AqlInspection", "AQL Inspection")]
    [InlineData("SupplierCorrectiveAction", "Supplier Corrective Action")]
    [InlineData("ManagementReview", "Management Review")]
    [InlineData("PpapSubmission", "PPAP Submission")]
    [InlineData("LpaTemplate", "LPA Template")]
    [InlineData("MrbReview", "MRB Review")]
    [InlineData("", "Record")]
    public void Labels_have_defaults_and_a_safe_fallback(string type, string label) => Assert.Equal(label, new RecordTypeLabels().For(type));

    [Fact]
    public void Configured_labels_override_defaults() =>
        Assert.Equal("FAI Report", new RecordTypeLabels(new Dictionary<string, string> { ["Fai"] = "FAI Report" }).For("fai"));

    [Fact]
    public async Task Record_type_labels_are_served_and_routes_and_messages_use_them()
    {
        var site = await app.CreateSiteAsync();
        var admin = await app.AdminAsync();
        var types = await admin.GetElAsync("/api/record-types");
        Assert.Contains(types.EnumerateArray(), t => t.Str("type") == "ChangeRequest" && t.Str("label") == "Change Request");
        Assert.Contains(types.EnumerateArray(), t => t.Str("type") == "Fai" && t.Str("label") == "First Article Inspection");
        var (_, viewer) = await app.CreateUserAsync([Permissions.NcrView], site);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/record-types")).StatusCode); // labels only, any signed-in user

        var routes = await admin.GetElAsync("/api/admin/config/approval-routes");
        Assert.Contains(routes.EnumerateArray(), r => r.Str("recordType") == "ChangeRequest" && r.Str("recordTypeLabel") == "Change Request");
        var fields = await admin.GetElAsync("/api/admin/config/workflow-designer/fields/Fai");
        Assert.Equal("Fai", fields.Str("recordType"));
        Assert.Equal("First Article Inspection", fields.Str("label"));

        var bad = await admin.PutAsync($"/api/admin/config/approval-routes/ChangeRequest?siteId={site}", new { name = "Bad", preventSelfApproval = true, steps = new object[] {
            new { order = 1, stage = 1, name = "Q", requiredPermission = Permissions.ChangesApprove, signatureMeaning = "Approved" },
            new { order = 2, stage = 2, name = "X", requiredPermission = Permissions.ChangesApprove, signatureMeaning = "Approved", conditionField = "NoSuchField", conditionOperator = "equals", conditionValue = "1" } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("is not a field of Change Request", await bad.Content.ReadAsStringAsync());
    }
}
