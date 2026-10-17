using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Infrastructure.Modules.Documents;
using QualGuardIQ.Infrastructure.Persistence;

namespace QualGuardIQ.Tests;

/// <summary>Gap 18: idempotent document type library seeding that keeps customer types, renames and deactivations.</summary>
[Collection("api")]
public sealed class GapClosureDocumentTypeTests(TestApp app)
{
    static readonly string[] Required = ["Quality Manual", "Policy", "Standard Operating Procedure", "Procedure", "Work Instruction", "Form", "Engineering Drawing", "Specification",
        "Inspection Instruction", "Test Method", "Control Plan", "Process Flow", "Training Material", "Customer Document", "Supplier Document", "External Standard", "Certificate",
        "Technical Data", "Maintenance Instruction", "Calibration Instruction", "Packaging Instruction", "Labeling Instruction", "Video", "Reference Document"];

    [Fact]
    public async Task Seed_is_complete_on_a_clean_database_and_idempotent_on_a_populated_one()
    {
        var admin = await app.AdminAsync();
        var site = await app.CreateSiteAsync();
        var docs = (await admin.GetElAsync("/api/document-types?kind=0")).EnumerateArray().Select(t => t.Str("name")).ToList();
        foreach (var n in Required) Assert.Contains(n, docs);
        Assert.Equal(docs.Count, docs.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains((await admin.GetElAsync("/api/document-types?kind=1")).EnumerateArray(), t => t.Str("name") == "Detail Drawing");

        // Populated database: a document filed under a customer category that predates the library, plus a customer type.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var owner = await db.Users.Select(u => u.Id).FirstAsync();
            db.Documents.Add(new ControlledDocument { Kind = DocumentKind.Document, Number = "LEGACY-" + Guid.NewGuid().ToString("N")[..6], Title = "Legacy", DocumentType = "Golden Sample Sheet", SiteId = site, OwnerId = owner });
            await db.SaveChangesAsync();
        }
        var custom = await (await admin.PostOkAsync("/api/document-types", new { kind = 0, name = "Customer Drawing Pack", description = "Customer supplied" })).JsonAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/document-types", new { kind = 0, name = "customer drawing pack" })).StatusCode);
        var all = (await admin.GetElAsync("/api/document-types?kind=0&includeInactive=true")).EnumerateArray().ToList();
        var video = all.First(t => t.Str("name") == "Video");
        var plan = all.First(t => t.Str("name") == "Plan");
        await admin.PutAsync($"/api/document-types/{video.Id()}", new { name = "Video Content", description = (string?)null, isActive = true, sortOrder = video.GetProperty("sortOrder").GetInt32() });
        await admin.PutAsync($"/api/document-types/{plan.Id()}", new { name = "Plan", description = (string?)null, isActive = false, sortOrder = 0 });

        var again = await (await admin.PostOkAsync("/api/document-types/seed")).JsonAsync();
        Assert.Equal(1, again.GetProperty("added").GetInt32()); // only the legacy category found on a document
        Assert.Equal(0, (await (await admin.PostOkAsync("/api/document-types/seed")).JsonAsync()).GetProperty("added").GetInt32());
        using (var scope = app.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<DocumentTypeService>().EnsureSeededAsync(default));
        var after = (await admin.GetElAsync("/api/document-types?kind=0&includeInactive=true")).EnumerateArray().ToList();
        Assert.Contains(after, t => t.Str("name") == "Golden Sample Sheet" && !t.GetProperty("isSystem").GetBoolean());
        Assert.Contains(after, t => t.Str("name") == "Customer Drawing Pack");
        Assert.Contains(after, t => t.Str("name") == "Video Content");
        Assert.DoesNotContain(after, t => t.Str("name") == "Video"); // rename kept, not re-added
        Assert.False(after.First(t => t.Str("name") == "Plan").GetProperty("isActive").GetBoolean()); // deactivation kept
        Assert.Equal(after.Count, after.Select(t => t.Str("name").ToLower()).Distinct().Count());
        Assert.DoesNotContain((await admin.GetElAsync("/api/document-types?kind=0")).EnumerateArray(), t => t.Str("name") == "Plan");

        // New documents use active library types (canonical spelling); retired and unknown types are refused.
        object Doc(string type) => new { kind = 0, title = "Typed doc", documentType = type, siteId = site, reviewIntervalMonths = 12 };
        var id = await (await admin.PostOkAsync("/api/documents", Doc("video content"))).Content.ReadFromJsonAsync<Guid>();
        Assert.Equal("Video Content", (await admin.GetElAsync($"/api/documents/{id}")).Str("documentType"));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/documents", Doc("Plan"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/documents", Doc("Not A Type"))).StatusCode);
        // A rename carries to documents of that type.
        await admin.PutAsync($"/api/document-types/{video.Id()}", new { name = "Training Video", description = (string?)null, isActive = true, sortOrder = 0 });
        Assert.Equal("Training Video", (await admin.GetElAsync($"/api/documents/{id}")).Str("documentType"));
        _ = custom;

        var (_, viewer) = await app.CreateUserAsync([QualGuardIQ.Domain.Security.Permissions.DocumentsView], site);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/document-types")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync("/api/document-types", new { kind = 0, name = "Nope" })).StatusCode);
    }
}
