using Microsoft.EntityFrameworkCore;
using QualGuardIQ.Domain.Messaging;

namespace QualGuardIQ.Infrastructure.Persistence;

public partial class AppDbContext
{
    public DbSet<MessagingProvider> MessagingProviders => Set<MessagingProvider>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<MessagingSubscription> MessagingSubscriptions => Set<MessagingSubscription>();
    public DbSet<MessageDelivery> MessageDeliveries => Set<MessageDelivery>();

    private static void ConfigureMessaging(ModelBuilder b)
    {
        b.Entity<MessagingProvider>(e =>
        {
            e.ToTable("messaging_providers"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Endpoint).HasMaxLength(500); e.Property(x => x.Sender).HasMaxLength(100);
            e.Property(x => x.SecretReference).HasMaxLength(200); e.Property(x => x.ConfigFingerprint).HasMaxLength(64); e.Property(x => x.LastTestFingerprint).HasMaxLength(64); e.Property(x => x.LastTestMessage).HasMaxLength(1000);
            Json(e, x => x.Keywords); e.HasIndex(x => x.Name).IsUnique();
            e.HasOne<MessageTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<MessageTemplate>(e => { e.ToTable("message_templates"); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Text).HasMaxLength(2000); e.Property(x => x.PushTitle).HasMaxLength(200); e.HasIndex(x => x.Name).IsUnique(); });
        b.Entity<MessagingSubscription>(e =>
        {
            e.ToTable("messaging_subscriptions"); e.Property(x => x.Address).HasMaxLength(500); e.Property(x => x.Label).HasMaxLength(100); e.Ignore(x => x.MaskedAddress);
            e.HasIndex(x => new { x.UserId, x.Channel, x.Address }).IsUnique();
        });
        b.Entity<MessageDelivery>(e =>
        {
            e.ToTable("message_deliveries"); e.Property(x => x.MaskedAddress).HasMaxLength(40); e.Property(x => x.Text).HasMaxLength(2000); e.Property(x => x.FailureReason).HasMaxLength(1000); e.Property(x => x.ProviderMessageId).HasMaxLength(200);
            e.HasOne<MessagingProvider>().WithMany().HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.State, x.NextAttemptAt }); e.HasIndex(x => new { x.ProviderId, x.SentAt }); e.HasIndex(x => new { x.ProviderId, x.ProviderMessageId });
            e.HasIndex(x => new { x.ProviderId, x.NotificationId, x.SubscriptionId }).IsUnique().HasFilter("\"NotificationId\" IS NOT NULL");
        });
    }
}
