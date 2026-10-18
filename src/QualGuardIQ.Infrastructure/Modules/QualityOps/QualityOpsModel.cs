using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.MasterData;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Quality;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<QualityEvent> QualityEvents => Set<QualityEvent>();
    public DbSet<QualityOpsSettings> QualityOpsSettings => Set<QualityOpsSettings>();
    public DbSet<Deviation> Deviations => Set<Deviation>();
    public DbSet<Investigation> Investigations => Set<Investigation>();
    public DbSet<ReleaseDecision> ReleaseDecisions => Set<ReleaseDecision>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();
    public DbSet<RegisterEntry> RegisterEntries => Set<RegisterEntry>();
    public DbSet<BusinessCalendar> BusinessCalendars => Set<BusinessCalendar>();
    public DbSet<CalendarHoliday> CalendarHolidays => Set<CalendarHoliday>();
    public DbSet<ServiceLevelPolicy> ServiceLevelPolicies => Set<ServiceLevelPolicy>();
    public DbSet<SkipLotRule> SkipLotRules => Set<SkipLotRule>();
    public DbSet<SkipLotEvent> SkipLotEvents => Set<SkipLotEvent>();
    public DbSet<QualityReview> QualityReviews => Set<QualityReview>();

    private static void ConfigureQualityOps(ModelBuilder b)
    {
        b.Entity<QualityOpsSettings>(e =>
        {
            foreach (var p in new[] { nameof(Domain.Quality.QualityOpsSettings.LikelihoodLevels), nameof(Domain.Quality.QualityOpsSettings.ImpactLevels), nameof(Domain.Quality.QualityOpsSettings.SeverityLevels),
                         nameof(Domain.Quality.QualityOpsSettings.Cells), nameof(Domain.Quality.QualityOpsSettings.Categories), nameof(Domain.Quality.QualityOpsSettings.Classifications) })
                e.Property(p).HasColumnType("jsonb");
            e.ToTable("quality_ops_settings", t => t.HasCheckConstraint("ck_quality_ops_settings_recurrence", "\"RecurrenceWindowDays\" > 0 AND \"SystemicThreshold\" > 1"));
        });
        b.Entity<QualityEvent>(e =>
        {
            e.ToTable("quality_events");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Source, x.ClientReference }).IsUnique().HasFilter("\"ClientReference\" IS NOT NULL");
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.HasIndex(x => new { x.PartId, x.Category, x.OccurredAt });
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.ClientReference).HasMaxLength(120);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(10000);
            foreach (var p in new[] { nameof(QualityEvent.Category), nameof(QualityEvent.Classification), nameof(QualityEvent.Severity), nameof(QualityEvent.LotNumber), nameof(QualityEvent.ExternalSystem), nameof(QualityEvent.EscalatedToType), nameof(QualityEvent.EscalatedToNumber) })
                e.Property(p).HasMaxLength(100);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.Process).HasMaxLength(200);
            e.Property(x => x.TriageNotes).HasMaxLength(4000);
            e.Property(x => x.ClosureNotes).HasMaxLength(4000);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReportedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TriagedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Deviation>(e =>
        {
            e.ToTable("deviations", t =>
            {
                t.HasCheckConstraint("ck_deviations_window", "\"EffectiveTo\" IS NULL OR \"EffectiveFrom\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
                t.HasCheckConstraint("ck_deviations_quantity", "\"QuantityLimit\" IS NULL OR \"QuantityLimit\" > 0");
            });
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.HasIndex(x => x.EffectiveTo);
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Requirement).HasMaxLength(4000);
            e.Property(x => x.DeviationDescription).HasMaxLength(10000);
            e.Property(x => x.Justification).HasMaxLength(10000);
            e.Property(x => x.RiskAssessment).HasMaxLength(10000);
            e.Property(x => x.ContainmentActions).HasMaxLength(4000);
            e.Property(x => x.AffectedLots).HasMaxLength(2000);
            e.Property(x => x.CustomerApprovalReference).HasMaxLength(200);
            e.Property(x => x.ClosureNotes).HasMaxLength(4000);
            e.Property(x => x.QuantityLimit).HasPrecision(18, 4);
            e.Property(x => x.QuantityUsed).HasPrecision(18, 4);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<QualityEvent>().WithMany().HasForeignKey(x => x.QualityEventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Investigation>(e =>
        {
            e.ToTable("investigations", t => t.HasCheckConstraint("ck_investigations_level", "\"EscalationLevel\" >= 0 AND \"EscalationLevel\" <= 5"));
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Scope).HasMaxLength(4000);
            foreach (var p in new[] { nameof(Investigation.Hypotheses), nameof(Investigation.Findings), nameof(Investigation.RootCause), nameof(Investigation.Conclusion) })
                e.Property(p).HasMaxLength(10000);
            e.Property(x => x.SampleReference).HasMaxLength(120);
            e.Property(x => x.TestMethod).HasMaxLength(200);
            e.Property(x => x.Specification).HasMaxLength(200);
            e.Property(x => x.ResultUnit).HasMaxLength(40);
            e.Property(x => x.LowerLimit).HasPrecision(18, 6);
            e.Property(x => x.UpperLimit).HasPrecision(18, 6);
            e.Property(x => x.Result).HasPrecision(18, 6);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<QualityEvent>().WithMany().HasForeignKey(x => x.QualityEventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReleaseDecision>(e =>
        {
            e.ToTable("release_decisions");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Status });
            e.HasIndex(x => x.LotId);
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.LotNumber).HasMaxLength(100);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.Checklist).HasColumnType("jsonb");
            e.Property(x => x.ReviewNotes).HasMaxLength(4000);
            e.Property(x => x.DecidedByName).HasMaxLength(200);
            e.Property(x => x.DecisionMeaning).HasMaxLength(200);
            e.Property(x => x.DecisionComment).HasMaxLength(4000);
            e.Property(x => x.SignatureHash).HasMaxLength(128);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Lot>().WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApprovalDelegation>(e =>
        {
            e.ToTable("approval_delegations", t =>
            {
                t.HasCheckConstraint("ck_approval_delegations_period", "\"EndsAt\" > \"StartsAt\"");
                t.HasCheckConstraint("ck_approval_delegations_people", "\"DelegatorId\" <> \"DelegateId\"");
            });
            e.HasIndex(x => new { x.DelegateId, x.EndsAt });
            e.HasIndex(x => new { x.DelegatorId, x.EndsAt });
            e.Property(x => x.RecordType).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DelegatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DelegateId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RegisterEntry>(e =>
        {
            e.ToTable("register_entries");
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.Kind, x.SiteId, x.Status });
            e.HasIndex(x => x.ExpiresOn);
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Status).HasMaxLength(60);
            e.Property(x => x.Summary).HasMaxLength(4000);
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<BusinessCalendar>(e =>
        {
            e.ToTable("business_calendars");
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.WorkingDays).HasMaxLength(20);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Holidays).WithOne().HasForeignKey(h => h.CalendarId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CalendarHoliday>(e =>
        {
            e.ToTable("calendar_holidays");
            e.HasIndex(x => new { x.CalendarId, x.Date }).IsUnique();
            e.Property(x => x.Name).HasMaxLength(200);
        });
        b.Entity<ServiceLevelPolicy>(e =>
        {
            e.ToTable("service_level_policies", t =>
            {
                t.HasCheckConstraint("ck_service_level_policies_target", "\"TargetHours\" > 0");
                t.HasCheckConstraint("ck_service_level_policies_warning", "\"WarningPercent\" BETWEEN 1 AND 99");
            });
            e.HasIndex(x => new { x.RecordType, x.Status });
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.RecordType).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(60);
            e.Property(x => x.EscalationPermission).HasMaxLength(100);
            e.HasOne<BusinessCalendar>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SkipLotRule>(e =>
        {
            e.ToTable("skip_lot_rules", t => t.HasCheckConstraint("ck_skip_lot_rules_counts", "\"QualifyAfterLots\" > 0 AND \"InspectEvery\" > 1 AND \"RequalifyAfterLots\" > 0"));
            e.HasIndex(x => new { x.SiteId, x.PartId, x.SupplierId }).IsUnique().AreNullsDistinct(false);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SkipLotEvent>(e =>
        {
            e.ToTable("skip_lot_events");
            e.HasIndex(x => new { x.RuleId, x.OccurredAt });
            e.Property(x => x.LotNumber).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne<SkipLotRule>().WithMany().HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<QualityReview>(e =>
        {
            e.ToTable("quality_reviews", t => t.HasCheckConstraint("ck_quality_reviews_period", "\"PeriodTo\" >= \"PeriodFrom\""));
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SiteId, x.Kind, x.Status });
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Metrics).HasColumnType("jsonb");
            e.Property(x => x.Conclusions).HasMaxLength(10000);
            e.Property(x => x.Actions).HasMaxLength(10000);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Part>().WithMany().HasForeignKey(x => x.PartId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
