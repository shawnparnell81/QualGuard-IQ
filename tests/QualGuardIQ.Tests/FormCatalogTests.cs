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
        Assert.Equal(24, codes.Count);
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
