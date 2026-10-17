using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Documents;

/// <summary>Template category (tree). System categories are seeded once; organizations add their own.</summary>
public class TemplateCategory : Entity, IAudited
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public string? ModuleKey { get; set; }
    public string? IndustryPackKey { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public string AuditLabel => $"template category {Code}";
}

public enum MappingConfidence { Unmapped = 0, Low = 1, Medium = 2, High = 3, Manual = 4 }
public enum ManualReviewStatus { NotRequired = 0, Pending = 1, Reviewed = 2 }

/// <summary>
/// Form definition of a form template revision (TECH 2 "Form definition"). The form schema itself is the revision's FieldSchema;
/// this row keeps the separate representations derived from it (layout, validation, formulas, print) and the configuration blocks.
/// Rows of approved revisions are immutable (code and database trigger).
/// </summary>
public class TemplateFormDefinition : Entity
{
    public Guid RevisionId { get; set; }
    public string LayoutSchema { get; set; } = "{}";
    public string ValidationSchema { get; set; } = "{}";
    public string FormulaSchema { get; set; } = "{}";
    public string WorkflowConfiguration { get; set; } = "{}";
    public string PrintConfiguration { get; set; } = "{}";
    public string SearchConfiguration { get; set; } = "{}";
    public string ReportingConfiguration { get; set; } = "{}";
    /// <summary>SHA-256 of the FieldSchema the rows were projected from.</summary>
    public string SchemaHash { get; set; } = "";
}

/// <summary>Relational section of a form definition (TECH 2 "Form section").</summary>
public class TemplateFormSection : Entity
{
    public Guid DefinitionId { get; set; }
    public Guid? ParentSectionId { get; set; }
    public string Key { get; set; } = "";
    public int Sequence { get; set; }
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    public string Layout { get; set; } = "{}";
    public bool IsRepeating { get; set; }
    public string? ConditionalRule { get; set; }
}

/// <summary>Relational field of a form definition (TECH 2 "Form field"). Table columns are fields with a TableId.</summary>
public class TemplateFormField : Entity
{
    public Guid DefinitionId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? TableId { get; set; }
    public string Key { get; set; } = "";
    public string? SourceFieldId { get; set; }
    public string Label { get; set; } = "";
    public string FieldType { get; set; } = "text";
    /// <summary>text, number, date, datetime, time, boolean, list, reference, file, none.</summary>
    public string DataType { get; set; } = "text";
    public int Sequence { get; set; }
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? ValidationRule { get; set; }
    public string? ConditionalRule { get; set; }
    public string? Formula { get; set; }
    public string Format { get; set; } = "{}";
    public string? SourceLocation { get; set; }
    public MappingConfidence MappingConfidence { get; set; }
    public ManualReviewStatus ManualReviewStatus { get; set; }
}

/// <summary>Relational table of a form definition (TECH 2 "Form table").</summary>
public class TemplateFormTable : Entity
{
    public Guid DefinitionId { get; set; }
    public Guid? SectionId { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int Sequence { get; set; }
    public int HeaderRows { get; set; } = 1;
    /// <summary>Fixed row count (checklists and matrices); null for rows added while filling in.</summary>
    public int? DataRows { get; set; }
    public bool IsRepeating { get; set; } = true;
    public string ColumnDefinitions { get; set; } = "[]";
    public string FormulaRules { get; set; } = "[]";
    public string SubtotalRules { get; set; } = "[]";
    public string TotalRules { get; set; } = "[]";
}

/// <summary>Per-user favourite templates in the catalog.</summary>
public class TemplateFavorite
{
    public Guid UserId { get; set; }
    public Guid DocumentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A saved list view (filters, sort, visible columns) for one user and one list.</summary>
public class SavedListView : Entity, IAudited
{
    public Guid UserId { get; set; }
    public string ListKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Settings { get; set; } = "{}";
    public bool IsDefault { get; set; }
    public string AuditLabel => $"saved view {Name}";
}
