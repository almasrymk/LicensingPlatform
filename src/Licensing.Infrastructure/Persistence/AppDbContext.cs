using System.Linq.Expressions;
using System.Text.Json;
using Licensing.Application.Abstractions;
using Licensing.Domain.Audit;
using Licensing.Domain.Catalog;
using Licensing.Domain.Customers;
using Licensing.Domain.Identity;
using Licensing.Domain.Integrations;
using Licensing.Domain.Licensing;
using Licensing.Domain.Reporting;
using Licensing.Domain.Subscriptions;
using Licensing.Domain.Tenants;
using Licensing.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Licensing.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : DbContext(options), IAppDbContext
{
    // Read by the global query filters on every query (EF parameterizes these per context instance).
    private Guid? ScopeTenantId => tenant.TenantId;
    private Guid? ScopeCustomerId => tenant.CustomerId;
    private bool ScopeUnrestricted => tenant.IsUnrestricted;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerContact> CustomerContacts => Set<CustomerContact>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionHistory> SubscriptionHistory => Set<SubscriptionHistory>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<LicenseActivation> LicenseActivations => Set<LicenseActivation>();
    public DbSet<ActivationAttempt> ActivationAttempts => Set<ActivationAttempt>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();
    public DbSet<UsageDaily> UsageDaily => Set<UsageDaily>();
    public DbSet<Licensing.Domain.Media.MediaFile> MediaFiles => Set<Licensing.Domain.Media.MediaFile>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite (tests only) cannot compare or order DateTimeOffset; store it as a sortable long there.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            builder.Properties<decimal>().HaveConversion<double>();
        }
        builder.Properties<string>().HaveMaxLength(256);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Ids are generated in the domain (Guid v7), never by the database: new children added through a navigation are inserts.
        foreach (var entity in b.Model.GetEntityTypes().Where(e => typeof(Entity).IsAssignableFrom(e.ClrType)))
            b.Entity(entity.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();

        // ---- Tenant isolation (plan Phase 0): every tenant-owned table gets a filter; customer-owned tables get a second one. ----
        foreach (var entity in b.Model.GetEntityTypes())
        {
            var clr = entity.ClrType;
            if (typeof(ICustomerOwned).IsAssignableFrom(clr))
                b.Entity(clr).HasQueryFilter(BuildFilter(clr, customer: true));
            else if (typeof(ITenantOwned).IsAssignableFrom(clr))
                b.Entity(clr).HasQueryFilter(BuildFilter(clr, customer: false));
        }

        // A customer user sees only its own customer record.
        b.Entity<Customer>().HasQueryFilter(c =>
            (ScopeUnrestricted && ScopeTenantId == null) ||
            (c.TenantId == ScopeTenantId && (ScopeCustomerId == null || c.Id == ScopeCustomerId)));

        // Contacts belong to a customer: hide other customers' contacts from customer users.
        b.Entity<CustomerContact>().HasQueryFilter(c =>
            (ScopeUnrestricted && ScopeTenantId == null) ||
            (c.TenantId == ScopeTenantId && (ScopeCustomerId == null || c.CustomerId == ScopeCustomerId)));
    }

    private LambdaExpression BuildFilter(Type clr, bool customer)
    {
        // e => (unrestricted && scopeTenant == null) || (e.TenantId == scopeTenant && (!customer || scopeCustomer == null || e.CustomerId == scopeCustomer))
        var e = Expression.Parameter(clr, "e");
        var ctx = Expression.Constant(this);
        var scopeTenant = Expression.Property(ctx, nameof(ScopeTenantId));
        var scopeCustomer = Expression.Property(ctx, nameof(ScopeCustomerId));
        var unrestricted = Expression.Property(ctx, nameof(ScopeUnrestricted));

        var platformWide = Expression.AndAlso(unrestricted, Expression.Equal(scopeTenant, Expression.Constant(null, typeof(Guid?))));
        var tenantMatch = Expression.Equal(Expression.Convert(Expression.Property(e, nameof(ITenantOwned.TenantId)), typeof(Guid?)), scopeTenant);
        Expression body = tenantMatch;
        if (customer)
        {
            var noCustomerScope = Expression.Equal(scopeCustomer, Expression.Constant(null, typeof(Guid?)));
            var customerMatch = Expression.Equal(Expression.Convert(Expression.Property(e, nameof(ICustomerOwned.CustomerId)), typeof(Guid?)), scopeCustomer);
            body = Expression.AndAlso(tenantMatch, Expression.OrElse(noCustomerScope, customerMatch));
        }
        return Expression.Lambda(Expression.OrElse(platformWide, body), e);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceTenantOnWrites();
        WriteDomainEventsToOutbox();
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException(Error.Conflict("CONCURRENCY_CONFLICT", "The record was changed by someone else. Reload and try again."));
        }
    }

    /// <summary>Defence in depth: even if a handler forgets, a tenant user can never write a row of another tenant.</summary>
    private void EnforceTenantOnWrites()
    {
        if (tenant.IsUnrestricted) return;
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted &&
                entry.Entity.TenantId != tenant.TenantId)
                throw new DomainException(Error.Forbidden("AUTH_FORBIDDEN", "Cross-tenant write rejected."));
        }
    }

    private void WriteDomainEventsToOutbox()
    {
        var aggregates = ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).Where(a => a.DomainEvents.Count > 0).ToList();
        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.DomainEvents)
            {
                OutboxMessages.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    Type = domainEvent.GetType().Name,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
                    TenantId = (aggregate as ITenantOwned)?.TenantId,
                    OccurredAt = domainEvent.OccurredAt,
                });
            }
            aggregate.ClearDomainEvents();
        }
    }

    public async Task<bool> TryReserveActivationSlotAsync(Guid licenseId, CancellationToken ct)
    {
        // Single conditional UPDATE: the database serializes concurrent attempts on the row, so the limit can never be exceeded.
        var updated = await Licenses
            .Where(l => l.Id == licenseId && (l.MaxActivations == null || l.ActiveActivations < l.MaxActivations))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ActiveActivations, l => l.ActiveActivations + 1), ct);
        return updated == 1;
    }

    public Task ReleaseActivationSlotAsync(Guid licenseId, CancellationToken ct) =>
        Licenses.Where(l => l.Id == licenseId && l.ActiveActivations > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ActiveActivations, l => l.ActiveActivations - 1), ct);
}
