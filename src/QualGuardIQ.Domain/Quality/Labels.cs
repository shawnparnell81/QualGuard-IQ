using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Domain.Quality;

/// <summary>What a label identifies. Each type is routed to a printer on its own.</summary>
public enum LabelType { Receiving = 0, Inventory = 1, Bin = 2, Lot = 3, Serial = 4, Carton = 5, Pallet = 6, Shipping = 7, Hold = 8, Quarantine = 9, Scrap = 10 }

/// <summary>
/// How a printer is reached. RawTcp sends ZPL to a network label printer (port 9100 by convention); HttpRelay posts the label to a print
/// agent at the site (for example on the workstation the printer is attached to); Virtual is a test printer that only records the output
/// (no physical device); ManualOnly never sends and leaves a downloadable PDF.
/// </summary>
public enum PrinterDriver { Virtual = 0, RawTcp = 1, HttpRelay = 2, ManualOnly = 3 }
public enum PrinterLanguage { Zpl = 0, Pdf = 1 }
public enum PrinterStatus { Unknown = 0, Online = 1, Offline = 2, Error = 3 }

/// <summary>A label printer at a site. Status reflects the last attempt or test; it never blocks a transaction.</summary>
public class LabelPrinter : Entity, IAudited, ISiteScoped
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public PrinterDriver Driver { get; set; }
    /// <summary>host:port for RawTcp, https URL for HttpRelay, a behaviour name for Virtual (ok, offline, error, flaky).</summary>
    public string? Address { get; set; }
    public PrinterLanguage Language { get; set; }
    public int Dpi { get; set; } = 203;
    public decimal WidthMm { get; set; } = 101.6m;
    public decimal HeightMm { get; set; } = 152.4m;
    /// <summary>Optional bearer token for an HttpRelay agent, encrypted at rest; never returned by the API.</summary>
    public string? SecretCiphertext { get; set; }
    public bool Enabled { get; set; } = true;
    public PrinterStatus Status { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public string? LastError { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"label printer {Code}";
}

/// <summary>Immediate = sent as soon as the transaction completes; Queue = held in the print queue until a person releases it.</summary>
public enum PrintMode { Immediate = 0, Queue = 1 }

/// <summary>
/// Which printer prints a label type. Conditions narrow a route: site, a warehouse or zone (any location in that subtree), a workstation
/// name and a user. The most specific active matching route wins (user, then workstation, then the deepest location, then site, then
/// organization-wide). AutoPrint routes print without anyone asking when the transaction happens; others only answer manual prints.
/// </summary>
public class LabelPrintRoute : Entity, IAudited
{
    public LabelType LabelType { get; set; }
    public Guid? SiteId { get; set; }
    public Guid? LocationId { get; set; }
    public string? Workstation { get; set; }
    public Guid? UserId { get; set; }
    public Guid PrinterId { get; set; }
    public int Copies { get; set; } = 1;
    public bool AutoPrint { get; set; }
    public PrintMode Mode { get; set; }
    public bool Active { get; set; } = true;
    public string AuditLabel => $"label route {LabelType}";
}

public enum PrintJobStatus { Pending = 0, Printing = 1, Printed = 2, Failed = 3, Cancelled = 4, ManualDownload = 5, Held = 6 }
public enum PrintTrigger { Auto = 0, Manual = 1, Reprint = 2 }

/// <summary>
/// One label print: the label content is frozen when the job is created (DataJson), so a reprint shows what was printed then. A failed
/// print never undoes the transaction that asked for it; it is retried with back-off and can always be downloaded as a PDF.
/// </summary>
public class LabelPrintJob : Entity, IAudited, ISiteScoped
{
    public LabelType LabelType { get; set; }
    public PrintTrigger Trigger { get; set; }
    public string RefType { get; set; } = "";
    public Guid RefId { get; set; }
    public string? RefNumber { get; set; }
    public string Title { get; set; } = "";
    public string Barcode { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public Guid? PrinterId { get; set; }
    public Guid? RouteId { get; set; }
    public int Copies { get; set; } = 1;
    public PrintJobStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public Guid RequestedById { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? PrintedAt { get; set; }
    public string? Workstation { get; set; }
    /// <summary>For automatic labels: the transaction that asked for them ("Receipt:&lt;id&gt;"), so a replayed request never prints twice.</summary>
    public string? TriggerKey { get; set; }
    public Guid? ReprintOfId { get; set; }
    public string? ReprintReason { get; set; }
    public Guid? LocationId { get; set; }
    public Guid SiteId { get; set; }
    public Guid? DepartmentId { get; set; }
    public string AuditLabel => $"{LabelType} label {RefNumber ?? Barcode}";
}
