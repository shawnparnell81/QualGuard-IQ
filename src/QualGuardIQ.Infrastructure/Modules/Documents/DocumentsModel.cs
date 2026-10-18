using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Documents;
using QualGuardIQ.Domain.Enterprise;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Domain.Shared;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<ControlledDocument> Documents => Set<ControlledDocument>();
    public DbSet<DocumentRevision> DocumentRevisions => Set<DocumentRevision>();
    public DbSet<DocumentTypeDefinition> DocumentTypes => Set<DocumentTypeDefinition>();
    public DbSet<TemplateCategory> TemplateCategories => Set<TemplateCategory>();
    public DbSet<TemplateFormDefinition> TemplateFormDefinitions => Set<TemplateFormDefinition>();
    public DbSet<TemplateFormSection> TemplateFormSections => Set<TemplateFormSection>();
    public DbSet<TemplateFormField> TemplateFormFields => Set<TemplateFormField>();
    public DbSet<TemplateFormTable> TemplateFormTables => Set<TemplateFormTable>();
    public DbSet<FormRecordValue> FormRecordValues => Set<FormRecordValue>();
    public DbSet<TemplateFavorite> TemplateFavorites => Set<TemplateFavorite>();
    public DbSet<SavedListView> SavedListViews => Set<SavedListView>();
    public DbSet<TemplatePackage> TemplatePackages => Set<TemplatePackage>();
    public DbSet<TemplatePackageItem> TemplatePackageItems => Set<TemplatePackageItem>();

    private static void ConfigureDocuments(ModelBuilder b)
    {
        b.Entity<DocumentTypeDefinition>(e =>
        {
            e.ToTable("document_types");
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.SeedKey).HasMaxLength(60);
            e.HasIndex(x => new { x.Kind, x.Name }).IsUnique();
            e.HasIndex(x => new { x.Kind, x.SeedKey }).IsUnique().HasFilter("\"SeedKey\" IS NOT NULL");
        });
        b.Entity<ControlledDocument>(e =>
        {
            e.ToTable("documents");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.DocumentType).HasMaxLength(60).IsRequired();
            e.Property(x => x.PartNumber).HasMaxLength(60);
            e.Property(x => x.DrawingSize).HasMaxLength(10);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Kind, x.SiteId, x.Status });
            e.HasIndex(x => x.PartNumber);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Revisions).WithOne(r => r.Document).HasForeignKey(r => r.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ModuleKey).HasMaxLength(100);
            e.Property(x => x.IndustryPack).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Confidentiality).HasMaxLength(40).IsRequired().HasDefaultValue("Internal");
            e.Property(x => x.Distribution).HasMaxLength(1000);
            e.Property(x => x.RelatedProcess).HasMaxLength(200);
            e.HasOne<TemplateCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RetentionPolicy>().WithMany().HasForeignKey(x => x.RetentionPolicyId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.ClonedFromId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CategoryId, x.Status });
            e.HasIndex(x => x.IndustryPack);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_doc_review", "\"ReviewIntervalMonths\" BETWEEN 1 AND 120");
                t.HasCheckConstraint("ck_doc_sheets", "\"SheetCount\" IS NULL OR \"SheetCount\" > 0");
                t.HasCheckConstraint("ck_doc_confidentiality", "\"Confidentiality\" IN ('Public', 'Internal', 'Confidential', 'Restricted')");
                t.HasCheckConstraint("ck_doc_source", "\"Source\" BETWEEN 0 AND 6");
            });
        });
        b.Entity<DocumentRevision>(e =>
        {
            e.ToTable("document_revisions");
            e.Property(x => x.Revision).HasMaxLength(10).IsRequired();
            e.Property(x => x.ChangeSummary).HasMaxLength(4000).IsRequired();
            e.Property(x => x.ObsoleteReason).HasMaxLength(1000);
            e.Property(x => x.FieldSchema).HasColumnType("jsonb");
            e.HasIndex(x => new { x.DocumentId, x.Revision }).IsUnique();
            e.HasIndex(x => new { x.DocumentId, x.Sequence }).IsUnique();
            // At most one effective revision and one open (draft or in review) revision per document.
            e.HasIndex(x => x.DocumentId, "ux_docrev_one_effective").IsUnique().HasFilter("\"Status\" = 3");
            e.HasIndex(x => x.DocumentId, "ux_docrev_one_open").IsUnique().HasFilter("\"Status\" IN (0, 1)");
            e.Property(x => x.ChangeReason).HasMaxLength(2000);
            e.Property(x => x.SchemaVersion).HasDefaultValue(1);
            e.Property(x => x.RenderingVersion).HasDefaultValue(1);
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.SourceFileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.NativeFileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.PreviewFileId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_docrev_superseded", "\"SupersededOn\" IS NULL OR \"EffectiveDate\" IS NULL OR \"SupersededOn\" >= \"EffectiveDate\"");
                t.HasCheckConstraint("ck_docrev_versions", "\"SchemaVersion\" > 0 AND \"RenderingVersion\" > 0");
                t.HasCheckConstraint("ck_docrev_source", "\"SourceType\" BETWEEN 0 AND 6");
            });
        });

        b.Entity<TemplateCategory>(e =>
        {
            e.ToTable("template_categories", t =>
            {
                t.HasCheckConstraint("ck_template_categories_code", "length(trim(\"Code\")) > 0");
                t.HasCheckConstraint("ck_template_categories_name", "length(trim(\"Name\")) > 0");
            });
            e.Property(x => x.Code).HasMaxLength(80).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.ModuleKey).HasMaxLength(100);
            e.Property(x => x.IndustryPackKey).HasMaxLength(100);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne<TemplateCategory>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TemplateFormDefinition>(e =>
        {
            e.ToTable("form_definitions");
            foreach (var p in new[] { nameof(TemplateFormDefinition.LayoutSchema), nameof(TemplateFormDefinition.ValidationSchema), nameof(TemplateFormDefinition.FormulaSchema),
                nameof(TemplateFormDefinition.WorkflowConfiguration), nameof(TemplateFormDefinition.PrintConfiguration), nameof(TemplateFormDefinition.SearchConfiguration), nameof(TemplateFormDefinition.ReportingConfiguration) })
                e.Property(p).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.SchemaHash).HasMaxLength(64).IsFixedLength().IsRequired();
            e.HasIndex(x => x.RevisionId).IsUnique();
            e.HasOne<DocumentRevision>().WithMany().HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TemplateFormSection>(e =>
        {
            e.ToTable("form_sections", t => t.HasCheckConstraint("ck_form_sections_sequence", "\"Sequence\" >= 0"));
            e.Property(x => x.Key).HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Instructions).HasMaxLength(4000);
            e.Property(x => x.Layout).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.ConditionalRule).HasColumnType("jsonb");
            e.HasIndex(x => new { x.DefinitionId, x.Key }).IsUnique();
            e.HasOne<TemplateFormDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TemplateFormSection>().WithMany().HasForeignKey(x => x.ParentSectionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TemplateFormTable>(e =>
        {
            e.ToTable("form_tables", t =>
            {
                t.HasCheckConstraint("ck_form_tables_rows", "\"HeaderRows\" >= 0 AND (\"DataRows\" IS NULL OR \"DataRows\" >= 0) AND \"Sequence\" >= 0");
            });
            e.Property(x => x.Key).HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            foreach (var p in new[] { nameof(TemplateFormTable.ColumnDefinitions), nameof(TemplateFormTable.FormulaRules), nameof(TemplateFormTable.SubtotalRules), nameof(TemplateFormTable.TotalRules) })
                e.Property(p).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.DefinitionId, x.Key }).IsUnique();
            e.HasOne<TemplateFormDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TemplateFormSection>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TemplateFormField>(e =>
        {
            e.ToTable("form_fields", t => t.HasCheckConstraint("ck_form_fields_sequence", "\"Sequence\" >= 0"));
            e.Property(x => x.Key).HasMaxLength(60).IsRequired();
            e.Property(x => x.SourceFieldId).HasMaxLength(250);
            e.Property(x => x.Label).HasMaxLength(300).IsRequired();
            e.Property(x => x.FieldType).HasMaxLength(30).IsRequired();
            e.Property(x => x.DataType).HasMaxLength(20).IsRequired();
            e.Property(x => x.DefaultValue).HasMaxLength(2000);
            e.Property(x => x.ValidationRule).HasColumnType("jsonb");
            e.Property(x => x.ConditionalRule).HasColumnType("jsonb");
            e.Property(x => x.Formula).HasMaxLength(4000);
            e.Property(x => x.Format).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.SourceLocation).HasMaxLength(250);
            // a key is unique among the form's top-level fields, and among the columns of one table
            e.HasIndex(x => new { x.DefinitionId, x.TableId, x.Key }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => new { x.SectionId, x.Sequence });
            e.HasOne<TemplateFormDefinition>().WithMany().HasForeignKey(x => x.DefinitionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TemplateFormSection>().WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TemplateFormTable>().WithMany().HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<FormRecordValue>(e =>
        {
            e.ToTable("form_record_values", t =>
            {
                t.HasCheckConstraint("ck_form_record_values_one", "num_nonnulls(\"TextValue\", \"NumericValue\", \"DateValue\", \"BooleanValue\", \"JsonValue\", \"FileId\") <= 1");
                t.HasCheckConstraint("ck_form_record_values_type", "\"ValueType\" IN ('text', 'number', 'date', 'boolean', 'json', 'file')");
            });
            e.Property(x => x.RowKey).HasMaxLength(100);
            e.Property(x => x.ValueType).HasMaxLength(10).IsRequired();
            e.Property(x => x.NumericValue).HasPrecision(30, 10);
            e.Property(x => x.JsonValue).HasColumnType("jsonb");
            e.HasIndex(x => new { x.FormRecordId, x.FieldId, x.RowKey }).IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => new { x.FieldId, x.NumericValue });
            e.HasOne<FormRecord>().WithMany().HasForeignKey(x => x.FormRecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TemplateFormField>().WithMany().HasForeignKey(x => x.FieldId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Attachment>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TemplateFavorite>(e =>
        {
            e.ToTable("template_favorites");
            e.HasKey(x => new { x.UserId, x.DocumentId });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SavedListView>(e =>
        {
            e.ToTable("saved_list_views");
            e.Property(x => x.ListKey).HasMaxLength(60).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Settings).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => new { x.UserId, x.ListKey, x.Name }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.ListKey }).IsUnique().HasFilter("\"IsDefault\"").HasDatabaseName("ux_saved_list_views_default");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TemplatePackage>(e =>
        {
            e.ToTable("template_packages");
            e.Property(x => x.PackageKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.Version).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(250).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.IndustryPackKey).HasMaxLength(100);
            e.Property(x => x.Manifest).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.FileSha256).HasMaxLength(64);
            e.HasIndex(x => new { x.PackageKey, x.Version, x.Origin }).IsUnique();
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InstalledById).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_tpkg_origin", "\"Origin\" BETWEEN 0 AND 2");
                t.HasCheckConstraint("ck_tpkg_key", "\"PackageKey\" ~ '^[a-z][a-z0-9-]{1,99}$'");
            });
        });
        b.Entity<TemplatePackageItem>(e =>
        {
            e.ToTable("template_package_items");
            e.Property(x => x.TemplateKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.CategoryCode).HasMaxLength(60).IsRequired();
            e.Property(x => x.ModuleKey).HasMaxLength(100);
            e.Property(x => x.DefinitionSha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.Definition).HasColumnType("jsonb");
            e.HasIndex(x => new { x.PackageId, x.TemplateKey }).IsUnique();
            e.HasIndex(x => x.TemplateKey);
            e.HasOne<ControlledDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.InstalledById).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureModules(ModelBuilder b)
    {
        ConfigureDocuments(b);
        ConfigureQuality(b);
        ConfigureMasterData(b);
        ConfigureComplaints(b);
        ConfigureInspection(b);
        ConfigureChanges(b);
        ConfigureRisks(b);
        ConfigureFai(b);
        ConfigureObjectives(b);
        ConfigureApqp(b);
        ConfigureIntegration(b);
        ConfigureInventory(b);
        ConfigureEnterprise(b);
        ConfigureEngineering(b);
        ConfigurePlatform(b);
        ConfigureStudio(b);
        ConfigureLabels(b); ConfigureCarrierIntegration(b); ConfigureShippingDocuments(b); ConfigureMessaging(b); ConfigureGenerationBoundary(b); ConfigureVideo(b); ConfigureSignInSecurity(b); ConfigureSso(b); ConfigureImports(b); ConfigureQualityOps(b); ConfigureFormMigration(b);
    }
}
