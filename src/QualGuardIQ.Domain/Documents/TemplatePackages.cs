using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Documents;

public enum TemplatePackageOrigin { Library = 0, Imported = 1, Exported = 2 }

/// <summary>Integrity of a package file: built-in library packages are trusted; imported files are hash-checked entry by entry.</summary>
public enum TemplatePackageIntegrity { BuiltIn = 0, HashesVerified = 1 }

/// <summary>
/// A versioned template package known to the organization (TECH 3 "Template packages", FI 5 marketplace): an industry pack from the
/// built-in library, a package file imported from another QualGuard IQ installation, or a package exported from this one.
/// </summary>
public class TemplatePackage : Entity, IAudited
{
    public string PackageKey { get; set; } = "";
    public string Version { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? IndustryPackKey { get; set; }
    public TemplatePackageOrigin Origin { get; set; }
    public TemplatePackageIntegrity Integrity { get; set; }
    /// <summary>The package manifest (format version, templates, their categories and definition hashes).</summary>
    public string Manifest { get; set; } = "{}";
    /// <summary>SHA-256 of the package file for imported and exported packages.</summary>
    public string? FileSha256 { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }
    public Guid? InstalledById { get; set; }
    public List<TemplatePackageItem> Items { get; set; } = [];
    public string AuditLabel => $"template package {PackageKey} {Version}";
}

/// <summary>A template carried by a package, and the form template it was installed as.</summary>
public class TemplatePackageItem : Entity
{
    public Guid PackageId { get; set; }
    public string TemplateKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string CategoryCode { get; set; } = "";
    public string? ModuleKey { get; set; }
    /// <summary>SHA-256 of the serialized definition.</summary>
    public string DefinitionSha256 { get; set; } = "";
    /// <summary>The definition itself for imported packages (library packages read it from the library).</summary>
    public string? Definition { get; set; }
    /// <summary>The form template the item was installed as, or for an exported package the template it was exported from.</summary>
    public Guid? DocumentId { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }
    public Guid? InstalledById { get; set; }
}
