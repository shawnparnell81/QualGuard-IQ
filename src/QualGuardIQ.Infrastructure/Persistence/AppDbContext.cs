using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Organization;
using QualGuardIQ.Domain.Security;
using QualGuardIQ.Infrastructure.Identity;

namespace QualGuardIQ.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
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

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("qms");

        foreach (var et in b.Model.GetEntityTypes().Where(t => typeof(Entity).IsAssignableFrom(t.ClrType)))
        {
            b.Entity(et.ClrType).Property(nameof(Entity.RowVersion)).IsRowVersion();
        }

        b.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        });

        b.Entity<OrganizationSettings>(e =>
        {
            e.ToTable("organization_settings");
            e.Property(x => x.ApplicationName).HasMaxLength(100).IsRequired();
            e.Property(x => x.OrganizationName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PrimaryColor).HasMaxLength(9);
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
}
