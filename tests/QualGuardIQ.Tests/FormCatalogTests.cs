using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Persistence;
using Xunit;

namespace QualGuardIQ.Tests;

/// <summary>Template catalog data (TECH 2): categories, the relational form definition, typed record values and immutable revisions in the database.</summary>
[Collection("api")]
public sealed class FormCatalogDataTests(TestApp app)
{
    static string Schema(string extraLabel = "Torque") => JsonSerializer.Serialize(new
    {
        schemaVersion = 2,
        sections = new object[] { new { key = "main", title = "Main", columns = 2 }, new { key = "sub", title = "Sub", parent = "main", columns = 1 } },
        fields = new object[]
        {
            new { key = "torque", label = extraLabel, type = "number", section = "main", required = true, min = 0, max = 100 },
            new { key = "checked_on", label = "Checked on", type = "date", section = "sub" },
            new { key = "ok", label = "OK", type = "boolean", section = "sub" },
        },
        tables = new object[]
        {
            new { key = "dims", title = "Dimensions", section = "main", columns = new object[] { new { key = "feature", label = "Feature", type = "text" }, new { key = "val", label = "Value", type = "number" } }, totals = new object[] { new { column = "val", function = "sum" } } }
        }
    });

    [Fact]
    public async Task System_categories_are_seeded()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var codes = await db.TemplateCategories.Where(c => c.IsSystem).Select(c => c.Code).ToListAsync();
        Assert.Equal(24 + QualGuardIQ.Infrastructure.Modules.Templates.TemplateLibrary.Categories.Count, codes.Count);
        Assert.Contains("FORM", codes);
        Assert.Contains("REFERENCE_DOCUMENT", codes);
    }

    [Fact]
    public async Task Drafts_project_to_relational_rows_records_index_typed_values_and_approved_revisions_are_locked_in_the_database()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var (approverId, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView], site);
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: "Projection check");
        var tpl = await author.GetDocumentAsync(tplId);
        var rev = tpl.Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = Schema("Torck") })).StatusCode);
        rev = (await author.GetDocumentAsync(tplId)).Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = Schema() })).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var def = await db.TemplateFormDefinitions.SingleAsync(d => d.RevisionId == rev.Id);
            var sections = await db.TemplateFormSections.Where(s => s.DefinitionId == def.Id).ToListAsync();
            Assert.Equal(2, sections.Count);
            Assert.Equal(sections.Single(s => s.Key == "main").Id, sections.Single(s => s.Key == "sub").ParentSectionId);
            var fields = await db.TemplateFormFields.Where(f => f.DefinitionId == def.Id).ToListAsync();
            Assert.Equal(5, fields.Count);
            Assert.Equal("Torque", fields.Single(f => f.Key == "torque").Label); // rebuilt on the second save
            Assert.Equal("number", fields.Single(f => f.Key == "torque").DataType);
            var table = await db.TemplateFormTables.SingleAsync(t => t.DefinitionId == def.Id);
            Assert.Equal(2, fields.Count(f => f.TableId == table.Id));
            Assert.Contains("\"sum\"", table.TotalRules);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync(tpl.Number))).StatusCode);

        var recId = (await (await author.PostOkAsync("/api/forms/records", new { templateId = tplId, siteId = site, departmentId = (Guid?)null, title = (string?)null })).JsonAsync()).Id();
        var rv = (await author.GetElAsync($"/api/forms/records/{recId}")).GetProperty("rowVersion").GetUInt32();
        var dims = JsonSerializer.Serialize(new { rows = new object[] { new { feature = "Bore", val = "10.5" }, new { feature = "Slot", val = "2" } } });
        Assert.Equal(HttpStatusCode.OK, (await author.PutAsync($"/api/forms/records/{recId}", new { values = new Dictionary<string, string?> { ["torque"] = "42.5", ["checked_on"] = "2026-10-01", ["ok"] = "true", ["dims"] = dims }, rowVersion = rv })).StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var r = await db.DocumentRevisions.AsNoTracking().SingleAsync(x => x.Id == rev.Id);
            Assert.Equal(approverId, r.ApprovedById);
            Assert.Equal(2, r.SchemaVersion);
            var vals = await db.FormRecordValues.Where(v => v.FormRecordId == recId).ToListAsync();
            var fields = await db.TemplateFormFields.Where(f => vals.Select(v => v.FieldId).Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Key);
            Assert.Equal(42.5m, vals.Single(v => fields[v.FieldId] == "torque").NumericValue);
            Assert.True(vals.Single(v => fields[v.FieldId] == "ok").BooleanValue);
            Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), vals.Single(v => fields[v.FieldId] == "checked_on").DateValue);
            Assert.Equal(10.5m, vals.Single(v => fields[v.FieldId] == "val" && v.RowKey == "0").NumericValue);
            Assert.Equal("Slot", vals.Single(v => fields[v.FieldId] == "feature" && v.RowKey == "1").TextValue);

            // the database itself refuses to change an approved revision or its form definition
            var e1 = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE qms.document_revisions SET \"FieldSchema\" = '[]' WHERE \"Id\" = {0}", rev.Id));
            Assert.Contains("locked", e1.MessageText);
            var e2 = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM qms.document_revisions WHERE \"Id\" = {0}", rev.Id));
            Assert.Contains("locked", e2.MessageText);
            var e3 = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE qms.form_fields SET \"Label\" = 'x' WHERE \"DefinitionId\" IN (SELECT \"Id\" FROM qms.form_definitions WHERE \"RevisionId\" = {0})", rev.Id));
            Assert.Contains("locked", e3.MessageText);
            // lifecycle columns stay changeable
            Assert.Equal(1, await db.Database.ExecuteSqlRawAsync("UPDATE qms.document_revisions SET \"ObsoleteReason\" = NULL WHERE \"Id\" = {0}", rev.Id));
        }
    }
}

/// <summary>Enterprise Template Library catalog (TECH 3): views, favorites, saved views, classification, clone, compare, retire and restore, usage and history.</summary>
[Collection("api")]
public sealed class TemplateLibraryTests(TestApp app)
{
    sealed record Item(Guid Id, string Number, string Title, string? Category, int Status, string? CurrentRevision, string? EffectiveRevision, int Source, bool Favorite, int Usage);
    sealed record PageOf(List<Item> Items, int Total);
    sealed record Cat(Guid Id, string Code, string Name, int Count);
    sealed record Diff(string Key, string Change, string? Before, string? After);
    sealed record Cmp(string From, string To, List<Diff> Fields);
    sealed record View(Guid Id, string Name, string Settings, bool IsDefault);

    static string Schema(string label) => JsonSerializer.Serialize(new { schemaVersion = 2, sections = new object[] { new { key = "main", title = "Main", columns = 1 } },
        fields = new object[] { new { key = "torque", label, type = "number", section = "main" } } });

    [Fact]
    public async Task Catalog_lists_by_view_classifies_favorites_clones_compares_retires_and_restores()
    {
        var site = await app.CreateSiteAsync();
        var (_, author) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsEdit, Permissions.DocumentsView, Permissions.DocumentsEdit], site);
        var (_, approver) = await app.CreateUserAsync([Permissions.FormsView, Permissions.FormsApprove, Permissions.DocumentsApprove, Permissions.DocumentsView], site);
        var (_, outsider) = await app.CreateUserAsync([Permissions.DocumentsView], site);
        var title = "Catalog " + Guid.NewGuid().ToString("N")[..6];
        var tplId = await author.CreateDocumentAsync(site, kind: 2, title: title);
        var rev = (await author.GetDocumentAsync(tplId)).Revisions.Single();
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{rev.Id}", new { changeSummary = "Initial", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = rev.RowVersion, fieldSchema = Schema("Torque") })).StatusCode);

        var drafts = await author.GetJsonAsync<PageOf>($"/api/template-library?view=draft&q={title}");
        Assert.Single(drafts!.Items);
        Assert.Empty((await author.GetJsonAsync<PageOf>($"/api/template-library?view=effective&q={title}")).Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync("/api/template-library")).StatusCode);

        var cats = await author.GetJsonAsync<List<Cat>>("/api/template-library/categories");
        var form = cats!.Single(c => c.Code == "FORM");
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/template-library/{tplId}/classification", new { categoryId = form.Id, moduleKey = "forms", industryPack = (string?)null, description = "Torque check", confidentiality = "Confidential", retentionPolicyId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await author.PutAsync($"/api/template-library/{tplId}/classification", new { categoryId = form.Id, confidentiality = "Secretish" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/template-library/{tplId}/favorite", new { favorite = true })).StatusCode);
        var favs = await author.GetJsonAsync<PageOf>($"/api/template-library?favorites=true&q={title}");
        Assert.True(favs!.Items.Single().Favorite);
        Assert.Equal("Controlled Forms", favs.Items.Single().Category);

        // approve revision A, then revise to B with a changed label and compare
        Assert.Equal(HttpStatusCode.NoContent, (await author.PostAsync($"/api/documents/revisions/{rev.Id}/submit", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await approver.SignAsync(await approver.PendingApprovalIdAsync((await author.GetDocumentAsync(tplId)).Number))).StatusCode);
        var eff = await author.GetJsonAsync<PageOf>($"/api/template-library?view=effective&q={title}");
        Assert.Equal("A", eff!.Items.Single().EffectiveRevision);
        var revB = await (await author.PostAsync($"/api/documents/{tplId}/revisions", new { revision = (string?)null, changeSummary = "Rename" })).Content.ReadFromJsonAsync<Guid>();
        var b = (await author.GetDocumentAsync(tplId)).Revisions.Single(r => r.Id == revB);
        Assert.Equal(HttpStatusCode.NoContent, (await author.PutAsync($"/api/documents/revisions/{revB}", new { changeSummary = "Rename", effectiveDate = (DateOnly?)null, trainingRequired = false, rowVersion = b.RowVersion, fieldSchema = Schema("Torque (Nm)") })).StatusCode);
        var cmp = await author.GetJsonAsync<Cmp>($"/api/template-library/{tplId}/compare?from={rev.Id}&to={revB}");
        Assert.Contains(cmp!.Fields, f => f.Key.EndsWith("torque") && f.Change == "Changed");

        // clone carries the effective definition into a new draft
        var cloneId = await (await author.PostAsync($"/api/template-library/{tplId}/clone", new { title = title + " copy", siteId = site, departmentId = (Guid?)null })).Content.ReadFromJsonAsync<Guid>();
        var clone = await author.GetElAsync($"/api/documents/{cloneId}");
        Assert.Contains("\"Torque\"", clone.GetProperty("revisions")[0].GetProperty("fieldSchema").GetString());
        var cloned = await author.GetJsonAsync<PageOf>($"/api/template-library?q={title} copy");
        Assert.Equal(6, cloned!.Items.Single().Source);

        // saved views
        var vid = await (await author.PostAsync("/api/template-library/views", new { name = "Mine", settings = "{\"view\":\"mine\"}", isDefault = true })).Content.ReadFromJsonAsync<Guid>();
        Assert.Single((await author.GetJsonAsync<List<View>>("/api/template-library/views"))!, v => v.Id == vid && v.IsDefault);
        Assert.Equal(HttpStatusCode.NoContent, (await author.DeleteAsync($"/api/template-library/views/{vid}")).StatusCode);

        var usage = await author.GetAsync($"/api/template-library/{tplId}/usage");
        Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
        var hist = await author.GetAsync($"/api/template-library/{tplId}/history");
        Assert.Equal(HttpStatusCode.OK, hist.StatusCode);
    }
}
