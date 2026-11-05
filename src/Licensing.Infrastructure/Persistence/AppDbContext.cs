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

    public void DiscardChanges() => ChangeTracker.Clear();

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct)
    {
        if (Database.CurrentTransaction is not null) return await work();
        await using var tx = await Database.BeginTransactionAsync(ct);
        var result = await work();
        await tx.CommitAsync(ct);
        return result;
    }

    // E3 (R1/R2, ADR-004): the subscription's device capacity is counted from the activations themselves, never from a
    // running counter. A device counts once per subscription however many of its licenses it holds, and only while its
    // activation is Active on a license that is neither revoked nor expired.
    private IQueryable<LicenseActivation> CountedActivations(Guid subscriptionId) =>
        LicenseActivations.IgnoreQueryFilters().Where(a => a.Status == ActivationStatus.Active
            && Licenses.IgnoreQueryFilters().Any(l => l.Id == a.LicenseId && l.SubscriptionId == subscriptionId
                && l.Status != LicenseStatus.Revoked && l.Status != LicenseStatus.Expired));

    /// <summary>
    /// Takes the subscription row's update lock for the rest of the transaction (a no-op UPDATE), so every reservation,
    /// release and recount of one subscription runs one at a time. SQLite takes its database write lock instead.
    /// </summary>
    private Task LockSubscriptionAsync(Guid subscriptionId, CancellationToken ct) =>
        Subscriptions.IgnoreQueryFilters().Where(s => s.Id == subscriptionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveDevices, x => x.ActiveDevices), ct);

    public async Task<bool> TryReserveActivationSlotAsync(Guid licenseId, string deviceId, CancellationToken ct)
    {
        if (Database.CurrentTransaction is null)
            throw new InvalidOperationException("Reserve an activation slot inside InTransactionAsync, with the activation's save.");

        var target = await Licenses.IgnoreQueryFilters().Where(l => l.Id == licenseId)
            .Select(l => new { l.SubscriptionId, l.MaxActivations }).FirstOrDefaultAsync(ct);
        if (target is null) return false;
        await LockSubscriptionAsync(target.SubscriptionId, ct);

        // A device that already holds a seat here takes nothing new (a concurrent duplicate then loses on the unique
        // LicenseId+DeviceId index and answers with the winner's seat).
        if (await LicenseActivations.IgnoreQueryFilters().AnyAsync(a => a.LicenseId == licenseId && a.DeviceId == deviceId && a.Status == ActivationStatus.Active, ct))
            return true;

        // The license's own optional limit counts its active seats.
        if (target.MaxActivations is { } licenseLimit
            && await LicenseActivations.IgnoreQueryFilters().CountAsync(a => a.LicenseId == licenseId && a.Status == ActivationStatus.Active, ct) >= licenseLimit)
            return false;

        // The subscription-wide limit counts distinct devices; a device the subscription already holds needs no new slot.
        var planLimit = await Subscriptions.IgnoreQueryFilters().Where(s => s.Id == target.SubscriptionId)
            .Select(s => Plans.IgnoreQueryFilters().Where(p => p.Id == s.PlanId).Select(p => p.MaxActivations).FirstOrDefault())
            .FirstOrDefaultAsync(ct);
        if (planLimit is null) return true;
        var counted = CountedActivations(target.SubscriptionId);
        if (await counted.AnyAsync(a => a.DeviceId == deviceId, ct)) return true;
        return await counted.Select(a => a.DeviceId).Distinct().CountAsync(ct) < planLimit;
    }

    public Task RecountDevicesAsync(Guid licenseId, CancellationToken ct) =>
        InTransactionAsync(async () =>
        {
            var subscriptionId = await Licenses.IgnoreQueryFilters().Where(l => l.Id == licenseId)
                .Select(l => (Guid?)l.SubscriptionId).FirstOrDefaultAsync(ct);
            if (subscriptionId is not { } id) return 0;
            await LockSubscriptionAsync(id, ct);
            return await RecountSubscriptionAsync(id, ct);
        }, ct);

    /// <summary>Writes the counters of one subscription and its licenses from the activations. Returns the rows it corrected.</summary>
    private async Task<int> RecountSubscriptionAsync(Guid subscriptionId, CancellationToken ct)
    {
        var devices = await CountedActivations(subscriptionId).Select(a => a.DeviceId).Distinct().CountAsync(ct);
        var repaired = await Subscriptions.IgnoreQueryFilters().Where(s => s.Id == subscriptionId && s.ActiveDevices != devices)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveDevices, devices), ct);

        var seats = await Licenses.IgnoreQueryFilters().Where(l => l.SubscriptionId == subscriptionId)
            .Select(l => new
            {
                l.Id,
                l.ActiveActivations,
                Actual = LicenseActivations.IgnoreQueryFilters().Count(a => a.LicenseId == l.Id && a.Status == ActivationStatus.Active),
            })
            .ToListAsync(ct);
        foreach (var seat in seats.Where(x => x.ActiveActivations != x.Actual))
            repaired += await Licenses.IgnoreQueryFilters().Where(l => l.Id == seat.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ActiveActivations, seat.Actual), ct);
        return repaired;
    }

    public async Task<int> ReconcileDeviceCountersAsync(int batchSize, CancellationToken ct)
    {
        // Finds subscriptions whose counters disagree with their activations, then repairs each under its own lock.
        var drifted = await Subscriptions.IgnoreQueryFilters()
            .Where(s => s.ActiveDevices != LicenseActivations.IgnoreQueryFilters()
                    .Where(a => a.Status == ActivationStatus.Active && Licenses.IgnoreQueryFilters().Any(l => l.Id == a.LicenseId
                        && l.SubscriptionId == s.Id && l.Status != LicenseStatus.Revoked && l.Status != LicenseStatus.Expired))
                    .Select(a => a.DeviceId).Distinct().Count()
                || Licenses.IgnoreQueryFilters().Any(l => l.SubscriptionId == s.Id && l.ActiveActivations
                    != LicenseActivations.IgnoreQueryFilters().Count(a => a.LicenseId == l.Id && a.Status == ActivationStatus.Active)))
            .OrderBy(s => s.Id).Select(s => s.Id).Take(batchSize).ToListAsync(ct);

        var repaired = 0;
        foreach (var id in drifted)
            repaired += await InTransactionAsync(async () =>
            {
                await LockSubscriptionAsync(id, ct);
                return await RecountSubscriptionAsync(id, ct);
            }, ct);
        return repaired;
    }
}
