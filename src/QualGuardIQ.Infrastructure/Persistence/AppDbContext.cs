using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Domain.Shared;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<OrganizationSettings> OrganizationSettings => Set<OrganizationSettings>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Role> QmsRoles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<RecordLink> RecordLinks => Set<RecordLink>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<RecordTag> RecordTags => Set<RecordTag>();
    public DbSet<ActionItem> ActionItems => Set<ActionItem>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldValues> CustomFieldValues => Set<CustomFieldValues>();
    public DbSet<SearchEntry> SearchEntries => Set<SearchEntry>();
    public DbSet<ApprovalRoute> ApprovalRoutes => Set<ApprovalRoute>();
    public DbSet<ApprovalRouteStep> ApprovalRouteSteps => Set<ApprovalRouteStep>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalDecision> ApprovalDecisions => Set<ApprovalDecision>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("qms");
        ConfigureShared(b);
        ConfigureModules(b);

        foreach (var et in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            b.Entity(et.ClrType).Property(nameof(Entity.RowVersion)).IsRowVersion();
        }
        // Ids are assigned in code (UUIDv7); tell EF so new children added through navigations are inserted, not updated.
        foreach (var et in b.Model.GetEntityTypes())
        {
            var pk = et.FindPrimaryKey();
            if (pk is { Properties.Count: 1 } && pk.Properties[0].ClrType == typeof(Guid) && pk.Properties[0].Name == "Id" && et.ClrType.Namespace!.StartsWith("QualGuardIQ.Domain"))
                pk.Properties[0].ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }

        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PortalKind).HasMaxLength(20);
            e.ToTable(t => t.HasCheckConstraint("ck_user_portal", "(\"PortalKind\" IS NULL AND \"PortalPartyId\" IS NULL) OR (\"PortalKind\" IN ('Customer','Supplier') AND \"PortalPartyId\" IS NOT NULL)"));
        });

        b.Entity<OrganizationSettings>(e =>
        {
            e.ToTable("organization_settings");
            e.Property(x => x.ApplicationName).HasMaxLength(100).IsRequired();
            e.Property(x => x.OrganizationName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PrimaryColor).HasMaxLength(9);
            e.Property(x => x.SupplierSuspendedPolicy).HasDefaultValue(QualGuardIQ.Domain.MasterData.SupplierRestriction.Block).HasSentinel((QualGuardIQ.Domain.MasterData.SupplierRestriction)(-1));
            e.Property(x => x.SupplierDisqualifiedPolicy).HasDefaultValue(QualGuardIQ.Domain.MasterData.SupplierRestriction.Block).HasSentinel((QualGuardIQ.Domain.MasterData.SupplierRestriction)(-1));
            e.Property(x => x.SupplierUnapprovedPolicy).HasDefaultValue(QualGuardIQ.Domain.MasterData.SupplierRestriction.Warn).HasSentinel((QualGuardIQ.Domain.MasterData.SupplierRestriction)(-1));
            e.Property(x => x.SupplierExpiredCertPolicy).HasDefaultValue(QualGuardIQ.Domain.MasterData.SupplierRestriction.Warn).HasSentinel((QualGuardIQ.Domain.MasterData.SupplierRestriction)(-1));
            e.ToTable(t => t.HasCheckConstraint("ck_org_color", "\"PrimaryColor\" IS NULL OR \"PrimaryColor\" ~ '^#[0-9A-Fa-f]{6}$'"));
        });

        b.Entity<Site>(e =>
        {
            e.ToTable("sites");
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<Department>(e =>
        {
            e.ToTable("departments");
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.SiteId, x.Code }).IsUnique();
            e.HasOne(x => x.Site).WithMany(s => s.Departments).HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions");
            e.HasKey(x => new { x.RoleId, x.Permission });
            e.Property(x => x.Permission).HasMaxLength(100);
        });

        b.Entity<UserRoleAssignment>(e =>
        {
            e.ToTable("user_role_assignments");
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.RoleId, x.SiteId, x.DepartmentId }).IsUnique().AreNullsDistinct(false);
            e.ToTable(t => t.HasCheckConstraint("ck_ura_dept_needs_site", "\"DepartmentId\" IS NULL OR \"SiteId\" IS NOT NULL"));
        });

        b.Entity<SecurityEvent>(e =>
        {
            e.ToTable("security_events");
            e.Property(x => x.EventType).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries");
            e.Property(x => x.Action).HasMaxLength(30).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
            e.Property(x => x.UserName).HasMaxLength(256).IsRequired();
            e.Property(x => x.Changes).HasColumnType("jsonb");
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.OccurredAt);
        });

        b.Entity<NumberSequence>(e =>
        {
            e.ToTable("number_sequences");
            e.Property(x => x.RecordType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Prefix).HasMaxLength(20);
            e.HasIndex(x => new { x.RecordType, x.SiteId }).IsUnique().AreNullsDistinct(false);
            e.ToTable(t => t.HasCheckConstraint("ck_numseq_next", "\"NextValue\" > 0 AND \"Padding\" BETWEEN 1 AND 10"));
        });
    }

    private static void ConfigureShared(ModelBuilder b)
    {
        b.Entity<Attachment>(e =>
        {
            e.ToTable("attachments");
            e.Property(x => x.OwnerType).HasMaxLength(50).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsRequired();
            e.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
            e.HasIndex(x => x.StorageKey).IsUnique();
            e.HasIndex(x => new { x.LineageId, x.Version }).IsUnique();
            e.ToTable(t => { t.HasCheckConstraint("ck_att_size", "\"SizeBytes\" > 0"); t.HasCheckConstraint("ck_att_version", "\"Version\" >= 1"); });
        });
        b.Entity<Comment>(e =>
        {
            e.ToTable("comments");
            e.Property(x => x.OwnerType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Body).HasMaxLength(10000).IsRequired();
            e.Property(x => x.AuthorName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
        });
        b.Entity<RecordLink>(e =>
        {
            e.ToTable("record_links");
            e.Property(x => x.FromType).HasMaxLength(50).IsRequired();
            e.Property(x => x.ToType).HasMaxLength(50).IsRequired();
            e.Property(x => x.LinkType).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.FromType, x.FromId, x.ToType, x.ToId, x.LinkType }).IsUnique();
            e.HasIndex(x => new { x.ToType, x.ToId });
            e.ToTable(t => t.HasCheckConstraint("ck_link_not_self", "NOT (\"FromType\" = \"ToType\" AND \"FromId\" = \"ToId\")"));
        });
        b.Entity<Tag>(e => { e.ToTable("tags"); e.Property(x => x.Name).HasMaxLength(50).IsRequired(); e.HasIndex(x => x.Name).IsUnique(); });
        b.Entity<RecordTag>(e =>
        {
            e.ToTable("record_tags");
            e.HasKey(x => new { x.OwnerType, x.OwnerId, x.TagId });
            e.Property(x => x.OwnerType).HasMaxLength(50);
            e.HasOne<Tag>().WithMany().HasForeignKey(x => x.TagId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ActionItem>(e =>
        {
            e.ToTable("actions");
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.ActionType).HasMaxLength(50).IsRequired();
            e.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.SourceType, x.SourceId });
            e.HasIndex(x => new { x.AssigneeId, x.Status });
            e.HasOne<Site>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReminderLog>(e => { e.Property(x => x.Key).HasMaxLength(200); e.HasIndex(x => x.Key).IsUnique(); });
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Link).HasMaxLength(300);
            e.HasIndex(x => new { x.UserId, x.ReadAt });
            e.HasIndex(x => x.EmailSentAt).HasFilter("\"EmailRequested\" AND \"EmailSentAt\" IS NULL");
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CustomFieldDefinition>(e =>
        {
            e.ToTable("custom_field_definitions");
            e.Property(x => x.RecordType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Key).HasMaxLength(50).IsRequired();
            e.Property(x => x.Label).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.RecordType, x.Key }).IsUnique();
        });
        b.Entity<CustomFieldValues>(e =>
        {
            e.ToTable("custom_field_values");
            e.Property(x => x.RecordType).HasMaxLength(50).IsRequired();
            e.HasIndex(x => new { x.RecordType, x.RecordId }).IsUnique();
            e.Property(x => x.Values).HasColumnType("jsonb").HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, string?>>(v, (JsonSerializerOptions?)null) ?? new(),
                new ValueComparer<Dictionary<string, string?>>(
                    (a, c) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => new Dictionary<string, string?>(v)));
        });
        b.Entity<SearchEntry>(e =>
        {
            e.ToTable("search_entries");
            e.HasKey(x => new { x.RecordType, x.RecordId });
            e.Property(x => x.RecordType).HasMaxLength(50);
            e.Property(x => x.Number).HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Status).HasMaxLength(40);
            e.Property(x => x.Url).HasMaxLength(300).IsRequired();
            e.Property(x => x.ViewPermission).HasMaxLength(100).IsRequired();
            e.Property(x => x.PermissionSiteKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.PermissionDepartmentKey).HasMaxLength(250);
            e.HasIndex(x => x.ViewPermission);
            e.HasIndex(x => x.PermissionSiteKey);
            e.HasIndex(x => x.PermissionDepartmentKey);
            e.HasIndex(x => x.Number);
        });
        b.Entity<ApprovalRoute>(e =>
        {
            e.ToTable("approval_routes");
            e.Property(x => x.RecordType).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.RecordType, "ux_route_active").IsUnique().HasFilter("\"IsActive\"");
            e.HasIndex(x => new { x.RecordType, x.Version }).IsUnique();
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(s => s.RouteId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ApprovalRouteStep>(e =>
        {
            e.ToTable("approval_route_steps");
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.RequiredPermission).HasMaxLength(100).IsRequired();
            e.Property(x => x.SignatureMeaning).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.RouteId, x.Order }).IsUnique();
        });
        b.Entity<ApprovalRequest>(e =>
        {
            e.ToTable("approval_requests");
            e.Property(x => x.RecordType).HasMaxLength(50).IsRequired();
            e.Property(x => x.RecordNumber).HasMaxLength(60).IsRequired();
            e.HasIndex(x => new { x.RecordType, x.RecordId });
            e.HasIndex(x => new { x.RecordType, x.RecordId }, "ux_approval_one_pending").IsUnique().HasFilter("\"Status\" = 0");
            e.HasIndex(x => x.Status);
            e.HasOne<ApprovalRoute>().WithMany().HasForeignKey(x => x.RouteId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Decisions).WithOne().HasForeignKey(d => d.RequestId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApprovalDecision>(e =>
        {
            e.ToTable("approval_decisions");
            e.Property(x => x.StepName).HasMaxLength(100).IsRequired();
            e.Property(x => x.SignerName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Meaning).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.RequestId, x.SignerId }).IsUnique();
        });
    }
}
