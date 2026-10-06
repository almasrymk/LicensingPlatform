using Licensing.Domain.Audit;
using Licensing.Domain.Catalog;
using Licensing.Domain.Customers;
using Licensing.Domain.Identity;
using Licensing.Domain.Integrations;
using Licensing.Domain.Licensing;
using Licensing.Domain.Reporting;
using Licensing.Domain.Subscriptions;
using Licensing.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Licensing.Infrastructure.Persistence;

// Tables are grouped per module with a schema, so modules stay separable later (modular monolith).

internal sealed class TenantConfig : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("Tenants", "tenants");
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Code).HasMaxLength(32);
        b.Property(x => x.SuspensionReason).HasMaxLength(500);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users", "identity");
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.TenantId);
        b.Property(x => x.PasswordHash).HasMaxLength(512);
        b.Property(x => x.Role).HasMaxLength(32);
        b.Property(x => x.PreferredLanguage).HasMaxLength(5);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class RefreshTokenConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens", "identity");
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.FamilyId });
        b.HasIndex(x => x.ExpiresAt);
        b.Property(x => x.TokenHash).HasMaxLength(128);
    }
}

internal sealed class CustomerConfig : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers", "customers");
        b.HasIndex(x => new { x.TenantId, x.Name });
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.TaxNumber).HasMaxLength(50);
        b.HasMany(x => x.Contacts).WithOne().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Contacts).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class CustomerContactConfig : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> b)
    {
        b.ToTable("CustomerContacts", "customers");
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Phone).HasMaxLength(50);
        b.Property(x => x.JobTitle).HasMaxLength(100);
    }
}

internal sealed class ProductConfig : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products", "catalog");
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Property(x => x.Code).HasMaxLength(32);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.PlatformsValue).HasColumnName("Platforms").HasMaxLength(200).HasDefaultValue("");
        b.Property(x => x.Icon).HasMaxLength(32);
        b.Ignore(x => x.Platforms);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class PlanConfig : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("Plans", "catalog");
        b.HasIndex(x => new { x.ProductId, x.Code, x.Version }).IsUnique();
        b.HasIndex(x => x.TenantId);
        b.Property(x => x.Code).HasMaxLength(32);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.FeaturesValue).HasColumnName("Features").HasMaxLength(2000);
        b.OwnsOne(x => x.Price, m =>
        {
            m.Property(p => p.Amount).HasColumnName("PriceAmount").HasPrecision(18, 2);
            m.Property(p => p.Currency).HasColumnName("PriceCurrency").HasMaxLength(3);
        });
        b.Navigation(x => x.Price).IsRequired();
        b.Ignore(x => x.Features);
        b.Ignore(x => x.ActivationLimit);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class SubscriptionConfig : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("Subscriptions", "subscriptions");
        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        b.HasIndex(x => new { x.Status, x.EndDate });
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.HasMany(x => x.History).WithOne().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class SubscriptionHistoryConfig : IEntityTypeConfiguration<SubscriptionHistory>
{
    public void Configure(EntityTypeBuilder<SubscriptionHistory> b)
    {
        b.ToTable("SubscriptionHistory", "subscriptions");
        b.Property(x => x.Details).HasMaxLength(500);
    }
}

internal sealed class LicenseConfig : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> b)
    {
        b.ToTable("Licenses", "licensing");
        b.HasIndex(x => x.ProductKeyHash).IsUnique();
        b.HasIndex(x => x.LicenseNumber).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CustomerId });
        b.HasIndex(x => x.SubscriptionId);
        b.HasIndex(x => new { x.Status, x.ExpiresAt });
        b.Property(x => x.ProductKeyHash).HasMaxLength(128);
        b.Property(x => x.ProductKeyPrefix).HasMaxLength(16);
        b.Property(x => x.LicenseNumber).HasMaxLength(32);
        b.Property(x => x.ProductCode).HasMaxLength(32);
        b.Property(x => x.PlanCode).HasMaxLength(32);
        b.Property(x => x.FeaturesValue).HasColumnName("Features").HasMaxLength(2000);
        b.Property(x => x.StatusReason).HasMaxLength(500);
        b.Ignore(x => x.Features);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class LicenseActivationConfig : IEntityTypeConfiguration<LicenseActivation>
{
    public void Configure(EntityTypeBuilder<LicenseActivation> b)
    {
        b.ToTable("LicenseActivations", "licensing");
        // One row per device per license: concurrent activations of the same device cannot create two slots.
        b.HasIndex(x => new { x.LicenseId, x.DeviceId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.Status });
        b.Property(x => x.DeviceId).HasMaxLength(128);
        b.Property(x => x.DeviceName).HasMaxLength(200);
        b.Property(x => x.AppVersion).HasMaxLength(50);
        b.Property(x => x.LastIpAddress).HasMaxLength(64);
        b.Property(x => x.OperatingSystem).HasMaxLength(100);
    }
}

internal sealed class ActivationAttemptConfig : IEntityTypeConfiguration<ActivationAttempt>
{
    public void Configure(EntityTypeBuilder<ActivationAttempt> b)
    {
        b.ToTable("ActivationAttempts", "licensing");
        b.HasIndex(x => new { x.TenantId, x.At });
        b.Property(x => x.DeviceId).HasMaxLength(128);
        b.Property(x => x.ProductKeyPrefix).HasMaxLength(16);
        b.Property(x => x.ErrorCode).HasMaxLength(64);
        b.Property(x => x.IpAddress).HasMaxLength(64);
    }
}

internal sealed class SigningKeyConfig : IEntityTypeConfiguration<SigningKey>
{
    public void Configure(EntityTypeBuilder<SigningKey> b)
    {
        b.ToTable("SigningKeys", "licensing");
        b.HasIndex(x => x.Kid).IsUnique();
        b.Property(x => x.Kid).HasMaxLength(64);
        b.Property(x => x.Algorithm).HasMaxLength(16);
        b.Property(x => x.PublicKeyPem).HasMaxLength(2000);
    }
}

internal sealed class ApiClientConfig : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> b)
    {
        b.ToTable("ApiClients", "integrations");
        b.HasIndex(x => x.ClientId).IsUnique();
        b.HasIndex(x => x.TenantId);
        b.Property(x => x.ClientId).HasMaxLength(64);
        b.Property(x => x.SecretHash).HasMaxLength(128);
        b.Property(x => x.ScopesValue).HasColumnName("Scopes").HasMaxLength(500);
        b.Ignore(x => x.Scopes);
        b.Ignore(x => x.DomainEvents);
    }
}

internal sealed class WebhookConfig : IEntityTypeConfiguration<Webhook>
{
    public void Configure(EntityTypeBuilder<Webhook> b)
    {
        b.ToTable("Webhooks", "integrations");
        b.Property(x => x.Url).HasMaxLength(1000);
        b.Property(x => x.EventsValue).HasColumnName("Events").HasMaxLength(1000);
        b.Property(x => x.SecretHash).HasMaxLength(128);
    }
}

internal sealed class AuditRecordConfig : IEntityTypeConfiguration<AuditRecord>
{
    public void Configure(EntityTypeBuilder<AuditRecord> b)
    {
        b.ToTable("AuditRecords", "audit");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.At });
        b.HasIndex(x => x.At);
        b.Property(x => x.Action).HasMaxLength(100);
        b.Property(x => x.EntityType).HasMaxLength(64);
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.ActorId).HasMaxLength(64);
        b.Property(x => x.ActorName).HasMaxLength(256);
        b.Property(x => x.Details).HasMaxLength(2000);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.Property(x => x.CorrelationId).HasMaxLength(64);
    }
}

internal sealed class UsageDailyConfig : IEntityTypeConfiguration<UsageDaily>
{
    public void Configure(EntityTypeBuilder<UsageDaily> b)
    {
        b.ToTable("UsageDaily", "reporting");
        b.HasIndex(x => new { x.TenantId, x.CustomerId, x.Day }).IsUnique();
    }
}

internal sealed class MediaFileConfig : IEntityTypeConfiguration<Licensing.Domain.Media.MediaFile>
{
    public void Configure(EntityTypeBuilder<Licensing.Domain.Media.MediaFile> b)
    {
        b.ToTable("MediaFiles", "media");
        b.HasIndex(x => new { x.OwnerType, x.OwnerId });
        b.Property(x => x.OwnerType).HasMaxLength(16);
        b.Property(x => x.ContentType).HasMaxLength(32);
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.Data).HasMaxLength(-1);
    }
}

internal sealed class OutboxConfig : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages", "messaging");
        b.HasIndex(x => new { x.ProcessedAt, x.DeadLettered, x.NextAttemptAt });
        b.HasIndex(x => new { x.TenantId, x.OccurredAt, x.Id });
        b.Property(x => x.Type).HasMaxLength(128);
        b.Property(x => x.Payload).HasMaxLength(-1);
        b.Property(x => x.LastError).HasMaxLength(2000);
    }
}

internal sealed class InboxConfig : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> b)
    {
        b.ToTable("InboxMessages", "messaging");
        b.HasKey(x => new { x.MessageId, x.Handler });
        b.Property(x => x.Handler).HasMaxLength(200);
    }
}

internal sealed class IdempotencyConfig : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.ToTable("IdempotencyRecords", "messaging");
        b.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        b.HasIndex(x => x.CreatedAt);
        b.Property(x => x.Scope).HasMaxLength(200);
        b.Property(x => x.Key).HasMaxLength(100);
        b.Property(x => x.Body).HasMaxLength(-1);
    }
}
