using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Common;
using QualGuardIQ.Domain.Organization;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<DeletedRecord> DeletedRecords => Set<DeletedRecord>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    /// <summary>
    /// Deleted records disappear from every query (lists, lookups, search, reports) through a filter on each deletable record type:
    /// a record is hidden while it has a deletion entry that hasn't been restored. Restoring closes the entry and the record is back.
    /// </summary>
    private void ConfigureRecordDeletion(ModelBuilder b)
    {
        b.Entity<DeletedRecord>(e =>
        {
            e.ToTable("deleted_records");
            e.Property(x => x.RecordType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Number).HasMaxLength(200).IsRequired();
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Status).HasMaxLength(100);
            e.Property(x => x.DeletePermission).HasMaxLength(100).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
            e.Property(x => x.DeletedByName).HasMaxLength(200).IsRequired();
            e.Property(x => x.RestoredByName).HasMaxLength(200);
            e.Property(x => x.RestoreReason).HasMaxLength(2000);
            e.HasIndex(x => x.RecordId).IsUnique().HasFilter("\"RestoredAt\" IS NULL").HasDatabaseName("ux_deleted_records_open");
            e.HasIndex(x => new { x.RecordType, x.RecordId });
            e.HasIndex(x => x.DeletedAt);
        });
        b.Entity<UserPreference>(e =>
        {
            e.ToTable("user_preferences");
            e.HasIndex(x => x.UserId).IsUnique();
            e.Property(x => x.DateFormat).HasMaxLength(20);
            e.Property(x => x.EmailNotifications).HasDefaultValue(true).HasSentinel(true);
        });
        b.Entity<OrganizationSettings>(e =>
        {
            e.Property(x => x.DateFormat).HasMaxLength(20).HasDefaultValue("auto");
            e.Property(x => x.EmailNotificationsEnabled).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.LogoContentType).HasMaxLength(20);
        });

        var apply = typeof(AppDbContext).GetMethod(nameof(HideDeleted), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        foreach (var et in b.Model.GetEntityTypes().Where(t => t.BaseType is null && typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)).ToList())
            apply.MakeGenericMethod(et.ClrType).Invoke(this, [b]);
    }

    private void HideDeleted<T>(ModelBuilder b) where T : Entity
    {
        Expression<Func<T, bool>> visible = x => !DeletedRecords.Any(d => d.RecordId == x.Id && d.RestoredAt == null);
        b.Entity<T>().HasQueryFilter(visible);
    }
}
