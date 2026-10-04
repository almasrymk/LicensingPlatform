using Licensing.Domain.Catalog;
using Licensing.Domain.Common;
using Licensing.Domain.Identity;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Licensing.Infrastructure.Security;
using Licensing.SharedKernel;
using Microsoft.Extensions.Options;

namespace Licensing.Tests.Unit;

public class SubscriptionStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Subscription InStatus(SubscriptionStatus status)
    {
        var sub = Subscription.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now,
            30, status == SubscriptionStatus.Trial ? 7 : null, Now);
        switch (status)
        {
            case SubscriptionStatus.Suspended: sub.Suspend("x", Now); break;
            case SubscriptionStatus.Cancelled: sub.Cancel("x", Now); break;
            case SubscriptionStatus.Expired: sub.TryExpire(Now.AddDays(60)); break;
        }
        Assert.Equal(status, sub.Status);
        return sub;
    }

    private static void Apply(Subscription sub, SubscriptionAction action)
    {
        switch (action)
        {
            case SubscriptionAction.Renew: sub.Renew(30, Now); break;
            case SubscriptionAction.ChangePlan: sub.ChangePlan(Guid.NewGuid(), 30, Now); break;
            case SubscriptionAction.Suspend: sub.Suspend(null, Now); break;
            case SubscriptionAction.Resume: sub.Resume(Now); break;
            case SubscriptionAction.Cancel: sub.Cancel(null, Now); break;
            case SubscriptionAction.Expire:
                if (!sub.TryExpire(Now.AddDays(400))) throw new DomainException(Error.Conflict("SUBSCRIPTION_INVALID_TRANSITION", "no-op"));
                break;
        }
    }

    public static TheoryData<SubscriptionStatus, SubscriptionAction> AllCombinations()
    {
        var data = new TheoryData<SubscriptionStatus, SubscriptionAction>();
        foreach (var s in Enum.GetValues<SubscriptionStatus>())
            foreach (var a in Enum.GetValues<SubscriptionAction>().Where(a => a != SubscriptionAction.Start))
                data.Add(s, a);
        return data;
    }

    /// <summary>Every allowed and every forbidden transition (plan Phase 2 exit gate).</summary>
    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Transition_is_allowed_only_when_the_state_machine_says_so(SubscriptionStatus status, SubscriptionAction action)
    {
        var sub = InStatus(status);
        var allowed = Subscription.AllowedActions(status).Contains(action);
        var versionBefore = sub.Version;

        if (allowed)
        {
            Apply(sub, action);
            Assert.True(sub.Version > versionBefore);
        }
        else
        {
            var ex = Assert.Throws<DomainException>(() => Apply(sub, action));
            Assert.Equal("SUBSCRIPTION_INVALID_TRANSITION", ex.Error.Code);
            Assert.Equal(status, sub.Status);
        }
    }

    [Fact]
    public void Start_with_trial_days_starts_in_trial_and_raises_started_event()
    {
        var sub = Subscription.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, 30, 14, Now);
        Assert.Equal(SubscriptionStatus.Trial, sub.Status);
        Assert.Equal(Now.AddDays(14), sub.EndDate);
        Assert.Contains(sub.DomainEvents, e => e is SubscriptionStartedV1 { IsTrial: true });
    }

    [Fact]
    public void Lifetime_subscription_never_expires()
    {
        var sub = Subscription.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, null, null, Now);
        Assert.True(sub.IsLifetime);
        Assert.False(sub.TryExpire(Now.AddYears(50)));
    }

    [Fact]
    public void Renew_of_active_subscription_extends_from_current_end_date()
    {
        var sub = Subscription.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, 30, null, Now);
        sub.Renew(30, Now.AddDays(10));
        Assert.Equal(Now.AddDays(60), sub.EndDate);
    }

    [Fact]
    public void Expire_is_idempotent_and_raises_one_event()
    {
        var sub = Subscription.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now, 30, null, Now);
        Assert.True(sub.TryExpire(Now.AddDays(31)));
        Assert.False(sub.TryExpire(Now.AddDays(32)));
        Assert.Single(sub.DomainEvents.OfType<SubscriptionExpiredV1>());
        Assert.Equal(2, sub.History.Count);
    }
}

public class CatalogTests
{
    private static Plan NewPlan() => Plan.Create(Guid.NewGuid(), Guid.NewGuid(), "pro", "Pro", new Money(10, "usd"), 365, null, 5, 24, 7,
        ["Reports", "inventory", "inventory"], DateTimeOffset.UtcNow);

    [Fact]
    public void Plan_normalizes_codes_and_features()
    {
        var plan = NewPlan();
        Assert.Equal("PRO", plan.Code);
        Assert.Equal(["inventory", "reports"], plan.Features);
        Assert.Equal("USD", plan.Price.Currency);
    }

    [Fact]
    public void Published_plan_is_immutable_and_new_version_copies_it()
    {
        var plan = NewPlan();
        plan.Publish(DateTimeOffset.UtcNow);
        var ex = Assert.Throws<DomainException>(() => plan.Update("x", new Money(1, "USD"), 1, null, 1, 1, 1, []));
        Assert.Equal("PLAN_NOT_DRAFT", ex.Error.Code);

        var v2 = plan.NewVersion(2, DateTimeOffset.UtcNow);
        Assert.Equal(PlanStatus.Draft, v2.Status);
        Assert.Equal(2, v2.Version);
        Assert.Equal(plan.Features, v2.Features);
    }

    [Theory]
    [InlineData(null, true, 1000)]
    [InlineData(5, true, 4)]
    [InlineData(5, false, 5)]
    public void Plan_limit_null_means_unlimited(int? limit, bool allowed, int current) =>
        Assert.Equal(allowed, PlanLimit.From(limit).Allows(current));

    [Theory]
    [InlineData("Bad Code")]
    [InlineData("1abc")]
    [InlineData("")]
    public void Invalid_feature_codes_are_rejected(string code) =>
        Assert.Throws<DomainException>(() => FeatureCode.Create(code));

    [Fact]
    public void Entitlements_are_readable_by_software()
    {
        var e = NewPlan().ToEntitlements("ACC");
        Assert.Equal("ACC", e.ProductCode);
        Assert.Equal(5, e.MaxActivations);
        Assert.Contains("inventory", e.Features);
    }

    [Fact]
    public void Value_objects_validate()
    {
        Assert.Throws<DomainException>(() => new Money(-1, "USD"));
        Assert.Throws<DomainException>(() => EmailAddress.Create("not-an-email"));
        Assert.Throws<DomainException>(() => new DateRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.True(new DateRange(DateTimeOffset.UtcNow.AddDays(-1), null).Contains(DateTimeOffset.UtcNow));
        Assert.Equal(new Money(1, "egp"), new Money(1, "EGP"));
    }
}

public class LicenseDomainTests
{
    private static License NewLicense(DateTimeOffset? expires = null) => License.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), "LIC-1", "hash", "PREFIX", new Entitlements("P", "PL", 1, ["a"], 2, 30, 24, 7), expires, DateTimeOffset.UtcNow);

    [Fact]
    public void Revoked_suspended_and_expired_licenses_are_not_usable()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(NewLicense().CheckUsable(now));
        Assert.Equal("LIC_EXPIRED", NewLicense(now.AddDays(-1)).CheckUsable(now)!.Code);

        var l = NewLicense();
        l.Suspend("x");
        Assert.Equal("LIC_SUSPENDED", l.CheckUsable(now)!.Code);
        l.Resume();
        l.Revoke("x", now);
        Assert.Equal("LIC_REVOKED", l.CheckUsable(now)!.Code);
        Assert.Throws<DomainException>(() => l.Resume());
    }

    [Theory]
    [InlineData("short", false)]
    [InlineData("DEVICE-0001", true)]
    [InlineData("device with spaces", false)]
    [InlineData("aa:bb:cc:dd:ee:ff", true)]
    public void Device_id_format_is_enforced(string id, bool valid) => Assert.Equal(valid, LicenseActivation.IsValidDeviceId(id));
}

public class IdentityTests
{
    [Fact]
    public void User_is_locked_out_after_five_failed_attempts()
    {
        var now = DateTimeOffset.UtcNow;
        var user = User.CreatePlatformAdmin("a@b.test", "A", "hash", now);
        for (var i = 0; i < User.MaxFailedAttempts - 1; i++) user.RegisterFailedLogin(now);
        Assert.False(user.IsLockedOut(now));
        user.RegisterFailedLogin(now);
        Assert.True(user.IsLockedOut(now));
        Assert.False(user.IsLockedOut(now.Add(User.LockoutDuration).AddSeconds(1)));
    }

    [Fact]
    public void Customer_user_role_cannot_be_changed_to_staff()
    {
        var user = User.CreateCustomerUser(Guid.NewGuid(), Guid.NewGuid(), "c@b.test", "C", "hash", DateTimeOffset.UtcNow);
        Assert.Throws<DomainException>(() => user.Update("C", Roles.TenantAdmin));
    }

    [Fact]
    public void Customer_users_only_get_read_permissions()
    {
        var perms = Permissions.ForRole(Roles.CustomerUser);
        Assert.All(perms, p => Assert.EndsWith(".read", p));
        Assert.Equal(Permissions.All.Length, Permissions.ForRole(Roles.PlatformAdmin).Count);
    }

    [Fact]
    public void Password_hash_verifies_and_is_salted()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var h1 = hasher.Hash("Secret123");
        Assert.NotEqual(h1, hasher.Hash("Secret123"));
        Assert.True(hasher.Verify("Secret123", h1));
        Assert.False(hasher.Verify("secret123", h1));
    }
}

public class ProductKeyTests
{
    private static readonly ProductKeyGenerator Generator =
        new(new HmacSecretHasher(Options.Create(new SecurityOptions { SecretPepper = "pepper" })));

    [Fact]
    public void Keys_have_the_expected_format_and_at_least_128_bits()
    {
        var (key, hash, prefix) = Generator.Generate();
        Assert.Matches("^[A-Z2-9]{6}(-[A-Z2-9]{6}){4}$", key);
        Assert.Equal(key[..6], prefix);
        Assert.DoesNotContain(key.Replace("-", ""), hash);
        Assert.True(30 * Math.Log2(32) >= 128);
    }

    [Fact]
    public void Keys_are_unique_and_hash_is_stable_after_normalization()
    {
        var keys = Enumerable.Range(0, 2000).Select(_ => Generator.Generate()).ToList();
        Assert.Equal(keys.Count, keys.Select(k => k.Key).Distinct().Count());

        var (key, hash, _) = keys[0];
        var typed = " " + key.ToLowerInvariant().Replace("-", " - ") + " ";
        Assert.Equal(hash, Generator.Hash(Generator.Normalize(typed)));
    }
}
