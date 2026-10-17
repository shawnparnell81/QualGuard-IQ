using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QualGuardIQ.Application.Abstractions;
using QualGuardIQ.Domain.Audit;
using QualGuardIQ.Domain.Common;

namespace QualGuardIQ.Infrastructure.Persistence;

/// <summary>
/// Stamps created/updated metadata and writes audit entries in the same transaction as the change,
/// so a record change is never saved without its audit entry.
/// </summary>
public sealed class AuditInterceptor(ICurrentUser user, IClock clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> Ignored = [nameof(Entity.RowVersion), nameof(Entity.UpdatedAt), nameof(Entity.UpdatedById), nameof(Entity.CreatedAt), nameof(Entity.CreatedById)];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (e.Context is not null) Apply(e.Context);
        return base.SavingChangesAsync(e, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData e, InterceptionResult<int> result)
    {
        if (e.Context is not null) Apply(e.Context);
        return base.SavingChanges(e, result);
    }

    private void Apply(DbContext ctx)
    {
        var now = clock.UtcNow;
        var entries = ctx.ChangeTracker.Entries<Entity>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        var audits = new List<AuditEntry>();
        foreach (var entry in entries.Where(x => x.Entity is IAppendOnly && x.State is EntityState.Modified or EntityState.Deleted))
            throw new AppException($"{entry.Metadata.ClrType.Name} entries are append-only; post a correcting entry instead.", 409);
        foreach (var entry in entries.Where(x => x.Entity is IContentLock && x.State is EntityState.Modified or EntityState.Deleted))
        {
            var lockable = (IContentLock)entry.Entity;
            if (!lockable.IsLockedState(entry.Property(lockable.StatusProperty).OriginalValue)) continue;
            if (entry.State == EntityState.Deleted)
                throw new AppException($"{entry.Metadata.ClrType.Name} is locked and cannot be deleted.", 409);
            var illegal = entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue) &&
                !Ignored.Contains(p.Metadata.Name) && !lockable.MutableWhenLocked.Contains(p.Metadata.Name)).Select(p => p.Metadata.Name).ToList();
            if (illegal.Count > 0)
                throw new AppException($"{entry.Metadata.ClrType.Name} is locked; {string.Join(", ", illegal)} cannot be changed.", 409);
        }
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedById = user.UserId;
            }
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedById = user.UserId;
            }
            if (entry.Entity is not IAudited audited) continue;

            var changes = Changes(entry);
            if (entry.State == EntityState.Modified && changes.Count == 0) continue;
            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Deleted => "Deleted",
                _ when changes.ContainsKey(nameof(IArchivable.IsArchived)) => (bool?)entry.Property(nameof(IArchivable.IsArchived)).CurrentValue == true ? "Archived" : "Restored",
                _ => "Updated"
            };
            var fieldList = entry.State == EntityState.Modified ? $": {string.Join(", ", changes.Keys)}" : "";
            audits.Add(new AuditEntry
            {
                OccurredAt = now,
                UserId = user.UserId,
                UserName = user.UserName,
                Action = action,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id,
                Description = $"{action} {audited.AuditLabel}{fieldList}",
                Changes = changes.Count == 0 ? null : JsonSerializer.Serialize(changes),
                SiteId = entry.Entity is ISiteScoped s ? s.SiteId : null,
                CorrelationId = user.CorrelationId
            });
        }
        if (audits.Count > 0) ctx.Set<AuditEntry>().AddRange(audits);
    }

    private static Dictionary<string, object?> Changes(EntityEntry<Entity> entry)
    {
        var d = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            if (Ignored.Contains(p.Metadata.Name)) continue;
            // Encrypted secrets: record that they changed, never the ciphertext.
            if (p.Metadata.Name.StartsWith("Encrypted", StringComparison.Ordinal))
            {
                if (entry.State == EntityState.Added ? p.CurrentValue is not null : entry.State == EntityState.Deleted || (p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)))
                    d[p.Metadata.Name] = p.CurrentValue is null ? "cleared" : "set (value not recorded)";
                continue;
            }
            switch (entry.State)
            {
                case EntityState.Added:
                    if (p.CurrentValue is not null) d[p.Metadata.Name] = new { to = p.CurrentValue };
                    break;
                case EntityState.Deleted:
                    d[p.Metadata.Name] = new { from = p.OriginalValue };
                    break;
                case EntityState.Modified when p.IsModified && !Equals(p.OriginalValue, p.CurrentValue):
                    d[p.Metadata.Name] = new { from = p.OriginalValue, to = p.CurrentValue };
                    break;
            }
        }
        return d;
    }
}
