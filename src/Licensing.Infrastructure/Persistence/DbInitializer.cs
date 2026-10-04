using Licensing.Application.Abstractions;
using Licensing.Domain.Catalog;
using Licensing.Domain.Common;
using Licensing.Domain.Customers;
using Licensing.Domain.Identity;
using Licensing.Domain.Integrations;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Licensing.Domain.Tenants;
using Licensing.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Licensing.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    /// <summary>Apply EF migrations on startup (development convenience; production applies them as a separate release step).</summary>
    public bool ApplyMigrations { get; set; }
    /// <summary>Bootstrap platform admin. Only created when no platform admin exists yet.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
    /// <summary>Load demo tenants, customers, plans, subscriptions and licenses. Never enable in production.</summary>
    public bool DemoData { get; set; }
}

public static class DemoAccounts
{
    public const string Password = "Demo@12345";
    public const string NourAdmin = "admin@nour.test";
    public const string NourOperator = "ops@nour.test";
    public const string OfoqAdmin = "admin@ofoq.test";
    public const string AlamalUser = "user@alamal.test";
    public const string ShifaUser = "user@alshifa.test";
    public const string SchoolUser = "user@almanar.test";
    public const string StoppedAdmin = "admin@stopped.test";

    public const string NourClientId = "lc_demo_nour";
    public const string NourClientSecret = "lcs_demo_nour_secret_change_me";
    public const string OfoqClientId = "lc_demo_ofoq";
    public const string OfoqClientSecret = "lcs_demo_ofoq_secret_change_me";

    // Demo product keys (only for local demos; real keys are random and shown once).
    public const string AlamalProKey = "AMLPRO-DEMQAA-KEY234-ACCNTG-PRQ2Q2";
    public const string AlamalPosKey = "AMLPOS-DEMQAA-KEY234-PQSPQS-BASZC2";
    public const string ShifaKey = "SHFAAC-DEMQAA-KEY234-BASXCC-SHFA22";
    public const string SultanTrialKey = "SLTNTR-DEMQAA-KEY234-TRXALL-SLTN22";
    public const string SchoolKey = "SCHMNR-DEMQAA-KEY234-SCHQQL-MNAR22";
}

public sealed class DbInitializer(
    AppDbContext db, TenantContext scope, IPasswordHasher passwords, ISecretHasher secrets, IProductKeyGenerator keys,
    IOptions<SeedOptions> options, TimeProvider clock, ILogger<DbInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        scope.RunAsSystem();
        var o = options.Value;

        if (db.Database.IsSqlServer() && o.ApplyMigrations)
            await db.Database.MigrateAsync(ct);
        else if (!db.Database.IsSqlServer())
            await db.Database.EnsureCreatedAsync(ct);

        await BootstrapAdminAsync(o, ct);
        if (o.DemoData && !await db.Tenants.AnyAsync(ct))
        {
            await SeedDemoAsync(ct);
            logger.LogWarning("Demo data loaded. Demo accounts use the password documented in README; never enable Seed:DemoData in production");
        }
        else if (o.DemoData)
        {
            await BackfillDemoAsync(ct);
        }
    }

    private static readonly Dictionary<string, string[]> DemoPlatforms = new()
    {
        ["ACCOUNTING"] = ["Windows", "Web"],
        ["POS"] = ["Windows", "Android"],
        ["SCHOOL"] = ["Web", "Windows", "macOS"],
    };

    private static string DemoOs(string deviceId) => deviceId switch
    {
        _ when deviceId.Contains("LAPTOP") => "Windows 11 Pro",
        _ when deviceId.Contains("POS") => "Windows 10 IoT",
        _ when deviceId.Contains("BRANCH") => "Windows Server 2022",
        _ when deviceId.Contains("OFFICE") => "Ubuntu 22.04",
        _ => "Windows 11",
    };

    /// <summary>Fills fields added after the demo data was first loaded (platforms, device OS). Idempotent.</summary>
    private async Task BackfillDemoAsync(CancellationToken ct)
    {
        var changed = false;
        foreach (var product in await db.Products.Where(p => p.PlatformsValue == "").ToListAsync(ct))
            if (DemoPlatforms.TryGetValue(product.Code, out var platforms))
            {
                product.Update(product.Name, product.Description, product.IsActive, platforms);
                changed = true;
            }
        foreach (var a in await db.LicenseActivations.Where(a => a.OperatingSystem == null).ToListAsync(ct))
        {
            a.Heartbeat(a.AppVersion, a.LastIpAddress, a.LastHeartbeatAt ?? a.ActivatedAt, DemoOs(a.DeviceId));
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    private async Task BootstrapAdminAsync(SeedOptions o, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(o.AdminEmail) || string.IsNullOrWhiteSpace(o.AdminPassword)) return;
        if (await db.Users.AnyAsync(u => u.Role == Roles.PlatformAdmin, ct)) return;
        db.Users.Add(User.CreatePlatformAdmin(o.AdminEmail, "مدير المنصة", passwords.Hash(o.AdminPassword), clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bootstrap platform admin created");
    }

    private async Task SeedDemoAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var pwd = passwords.Hash(DemoAccounts.Password);

        // ---------- Tenant 1: a software vendor with two products ----------
        var nour = Tenant.Create("شركة النور للبرمجيات", "NOUR", "info@nour.test", now.AddDays(-400));
        var ofoq = Tenant.Create("شركة الأفق للحلول التعليمية", "OFOQ", "info@ofoq.test", now.AddDays(-200));
        var stopped = Tenant.Create("شركة الريادة (موقوفة)", "RIYADA", "info@stopped.test", now.AddDays(-300));
        stopped.Suspend("عدم سداد الاشتراك", now.AddDays(-10));
        db.Tenants.AddRange(nour, ofoq, stopped);

        var accounting = Product.Create(nour.Id, "ACCOUNTING", "نظام المحاسبة", "برنامج محاسبة متكامل للشركات", now.AddDays(-390), DemoPlatforms["ACCOUNTING"]);
        var pos = Product.Create(nour.Id, "POS", "نقطة البيع", "برنامج كاشير ونقاط بيع", now.AddDays(-380), DemoPlatforms["POS"]);
        var school = Product.Create(ofoq.Id, "SCHOOL", "نظام إدارة المدارس", "إدارة الطلاب والدرجات والحضور", now.AddDays(-190), DemoPlatforms["SCHOOL"]);
        db.Products.AddRange(accounting, pos, school);

        var accBasic = Plan.Create(nour.Id, accounting.Id, "BASIC", "الأساسية - سنوي", new Money(1200, "EGP"), 365, null, 2, 24, 7,
            ["invoices", "reports.basic"], now.AddDays(-390));
        var accPro = Plan.Create(nour.Id, accounting.Id, "PRO", "الاحترافية - سنوي", new Money(3500, "EGP"), 365, null, 5, 24, 14,
            ["invoices", "reports.basic", "reports.advanced", "inventory", "multi-branch"], now.AddDays(-390));
        var accLifetime = Plan.Create(nour.Id, accounting.Id, "LIFETIME", "مدى الحياة", new Money(15000, "EGP"), null, null, 1, 72, 30,
            ["invoices", "reports.basic", "reports.advanced"], now.AddDays(-390));
        var posMonthly = Plan.Create(nour.Id, pos.Id, "MONTHLY", "نقطة البيع - شهري", new Money(300, "EGP"), 30, 14, 3, 12, 3,
            ["sales", "barcode", "receipts"], now.AddDays(-380));
        var accDraft = Plan.Create(nour.Id, accounting.Id, "ENTERPRISE", "المؤسسات (مسودة)", new Money(9000, "EGP"), 365, null, null, 24, 14,
            ["invoices", "reports.basic", "reports.advanced", "inventory", "multi-branch", "api"], now.AddDays(-5));
        var schoolYear = Plan.Create(ofoq.Id, school.Id, "YEAR", "سنة دراسية", new Money(5000, "EGP"), 300, null, 10, 24, 10,
            ["students", "grades", "attendance"], now.AddDays(-190));
        foreach (var p in new[] { accBasic, accPro, accLifetime, posMonthly, schoolYear }) p.Publish(now.AddDays(-370));
        db.Plans.AddRange(accBasic, accPro, accLifetime, posMonthly, accDraft, schoolYear);

        var alamal = Customer.Create(nour.Id, "مؤسسة الأمل التجارية", "info@alamal.test", "01000000001", "مصر", "100-200-300", now.AddDays(-360));
        alamal.AddContact("أحمد علي", "ahmed@alamal.test", "01000000011", "مدير الحسابات", true);
        var shifa = Customer.Create(nour.Id, "صيدليات الشفاء", "info@alshifa.test", "01000000002", "مصر", null, now.AddDays(-300));
        shifa.AddContact("منى حسن", "mona@alshifa.test", "01000000022", "مدير تقنية المعلومات", true);
        var sultan = Customer.Create(nour.Id, "مطاعم السلطان", "info@sultan.test", "01000000003", "السعودية", null, now.AddDays(-20));
        var oldCustomer = Customer.Create(nour.Id, "شركة الوادي (غير نشط)", "info@wadi.test", null, "مصر", null, now.AddDays(-500));
        oldCustomer.SetStatus(CustomerStatus.Inactive);
        var almanar = Customer.Create(ofoq.Id, "مدارس المنار", "info@almanar.test", "01000000004", "مصر", null, now.AddDays(-180));
        almanar.AddContact("خالد محمود", "khaled@almanar.test", null, "مدير المدرسة", true);
        var alnokhba = Customer.Create(ofoq.Id, "مدارس النخبة", "info@alnokhba.test", null, "الإمارات", null, now.AddDays(-90));
        db.Customers.AddRange(alamal, shifa, sultan, oldCustomer, almanar, alnokhba);

        // ---------- Users: platform admin is bootstrapped from config; one user per role per tenant ----------
        db.Users.AddRange(
            User.CreateTenantUser(nour.Id, DemoAccounts.NourAdmin, "مدير شركة النور", pwd, Roles.TenantAdmin, now),
            User.CreateTenantUser(nour.Id, DemoAccounts.NourOperator, "موظف الدعم - النور", pwd, Roles.TenantOperator, now),
            User.CreateTenantUser(ofoq.Id, DemoAccounts.OfoqAdmin, "مدير شركة الأفق", pwd, Roles.TenantAdmin, now),
            User.CreateTenantUser(stopped.Id, DemoAccounts.StoppedAdmin, "مدير شركة الريادة", pwd, Roles.TenantAdmin, now),
            User.CreateCustomerUser(nour.Id, alamal.Id, DemoAccounts.AlamalUser, "مستخدم مؤسسة الأمل", pwd, now),
            User.CreateCustomerUser(nour.Id, shifa.Id, DemoAccounts.ShifaUser, "مستخدم صيدليات الشفاء", pwd, now),
            User.CreateCustomerUser(ofoq.Id, almanar.Id, DemoAccounts.SchoolUser, "مستخدم مدارس المنار", pwd, now));

        db.ApiClients.AddRange(
            ApiClient.Create(nour.Id, "تطبيق سطح المكتب - النور", DemoAccounts.NourClientId, secrets.Hash(DemoAccounts.NourClientSecret), ApiScopes.All, now),
            ApiClient.Create(ofoq.Id, "تطبيق المدارس - الأفق", DemoAccounts.OfoqClientId, secrets.Hash(DemoAccounts.OfoqClientSecret),
                [ApiScopes.LicensesActivate, ApiScopes.LicensesValidate], now));

        await db.SaveChangesAsync(ct);

        // ---------- Subscriptions and licenses covering every state ----------
        var alamalPro = Sub(alamal, accPro, now.AddDays(-340));              // active, ends in ~25 days (expiring soon)
        var alamalPos = Sub(alamal, posMonthly, now.AddDays(-2), trial: false); // active monthly
        var shifaBasic = Sub(shifa, accBasic, now.AddDays(-200));            // active
        var shifaOld = Sub(shifa, accBasic, now.AddDays(-400));              // expired last month
        shifaOld.TryExpire(now);
        var sultanTrial = Sub(sultan, posMonthly, now.AddDays(-5), trial: true); // trial, ends in 9 days
        var sultanLifetime = Sub(sultan, accLifetime, now.AddDays(-15));     // lifetime
        var almanarYear = Sub(almanar, schoolYear, now.AddDays(-294));       // active, ends in 6 days
        var nokhbaYear = Sub(alnokhba, schoolYear, now.AddDays(-60));
        nokhbaYear.Suspend("متأخرات سداد", now.AddDays(-3));
        db.Subscriptions.AddRange(alamalPro, alamalPos, shifaBasic, shifaOld, sultanTrial, sultanLifetime, almanarYear, nokhbaYear);
        await db.SaveChangesAsync(ct);

        var lAlamalPro = Lic(alamalPro, accPro, accounting, DemoAccounts.AlamalProKey, now.AddDays(-340));
        var lAlamalPos = Lic(alamalPos, posMonthly, pos, DemoAccounts.AlamalPosKey, now.AddDays(-2));
        var lShifa = Lic(shifaBasic, accBasic, accounting, DemoAccounts.ShifaKey, now.AddDays(-200));
        var lShifaOld = Lic(shifaOld, accBasic, accounting, null, now.AddDays(-400));
        lShifaOld.TryExpire(now, force: true);
        var lShifaRevoked = Lic(shifaBasic, accBasic, accounting, null, now.AddDays(-100));
        lShifaRevoked.Revoke("تسريب المفتاح على الإنترنت", now.AddDays(-50));
        var lSultanTrial = Lic(sultanTrial, posMonthly, pos, DemoAccounts.SultanTrialKey, now.AddDays(-5));
        var lSultanLife = Lic(sultanLifetime, accLifetime, accounting, null, now.AddDays(-15));
        lSultanLife.Suspend("مراجعة الدفع");
        var lSchool = Lic(almanarYear, schoolYear, school, DemoAccounts.SchoolKey, now.AddDays(-294));
        db.Licenses.AddRange(lAlamalPro, lAlamalPos, lShifa, lShifaOld, lShifaRevoked, lSultanTrial, lSultanLife, lSchool);
        await db.SaveChangesAsync(ct);

        // Activations (devices) with realistic heartbeats.
        await Activate(lAlamalPro, "ALAMAL-PC-0001", "جهاز المحاسب الرئيسي", now.AddDays(-300), now.AddHours(-3));
        await Activate(lAlamalPro, "ALAMAL-PC-0002", "جهاز الفرع الثاني", now.AddDays(-120), now.AddHours(-30));
        await Activate(lAlamalPro, "ALAMAL-LAPTOP-03", "لابتوب المدير", now.AddDays(-30), now.AddDays(-6));
        await Activate(lAlamalPos, "ALAMAL-POS-0001", "كاشير 1", now.AddDays(-2), now.AddHours(-1));
        await Activate(lShifa, "SHIFA-BRANCH-001", "فرع المعادي", now.AddDays(-190), now.AddHours(-5));
        await Activate(lShifa, "SHIFA-BRANCH-002", "فرع مدينة نصر", now.AddDays(-150), now.AddHours(-2));
        await Activate(lSultanTrial, "SULTAN-POS-0001", "كاشير الفرع الرئيسي", now.AddDays(-5), now.AddHours(-4));
        await Activate(lSchool, "MANAR-OFFICE-001", "مكتب شؤون الطلاب", now.AddDays(-280), now.AddHours(-8));

        // Failed activation attempts for the reports.
        var rnd = new Random(42);
        string[] codes = ["LIC_ACTIVATION_LIMIT_REACHED", "LIC_INVALID_LICENSE", "LIC_REVOKED", "LIC_EXPIRED", "LIC_INVALID_LICENSE"];
        for (var i = 0; i < 18; i++)
        {
            var target = i % 3 == 0 ? lShifaRevoked : i % 3 == 1 ? lAlamalPro : null;
            db.ActivationAttempts.Add(ActivationAttempt.Record(nour.Id, target, target?.ProductKeyPrefix ?? "UNKNWN",
                $"DEVICE-{rnd.Next(1000, 9999)}", codes[i % codes.Length], $"41.33.{rnd.Next(1, 254)}.{rnd.Next(1, 254)}", now.AddDays(-rnd.Next(0, 13)).AddHours(-rnd.Next(0, 23))));
        }
        for (var i = 0; i < 25; i++)
            db.ActivationAttempts.Add(ActivationAttempt.Record(nour.Id, lAlamalPro, lAlamalPro.ProductKeyPrefix, $"ALAMAL-PC-000{1 + i % 2}", null,
                "41.33.10.10", now.AddDays(-rnd.Next(0, 13)).AddHours(-rnd.Next(0, 23))));
        await db.SaveChangesAsync(ct);

        Subscription Sub(Customer c, Plan p, DateTimeOffset start, bool trial = false) =>
            Subscription.Start(c.TenantId, c.Id, p.ProductId, p.Id, start, p.DurationDays, trial ? p.TrialDays : null, start);

        License Lic(Subscription s, Plan p, Product product, string? plainKey, DateTimeOffset issuedAt)
        {
            string hash, prefix;
            if (plainKey is null)
            {
                (_, hash, prefix) = keys.Generate();
            }
            else
            {
                var n = keys.Normalize(plainKey);
                hash = keys.Hash(n);
                prefix = n[..6];
            }
            var number = $"LIC-{issuedAt:yyyy}-{Convert.ToHexString(Guid.NewGuid().ToByteArray())[..8]}";
            return License.Issue(s.TenantId, s.CustomerId, s.Id, s.ProductId, p.Id, number, hash, prefix,
                p.ToEntitlements(product.Code), s.EndDate, issuedAt);
        }

        async Task Activate(License l, string deviceId, string name, DateTimeOffset at, DateTimeOffset lastHeartbeat)
        {
            var a = LicenseActivation.Create(l, deviceId, name, "2.4.1", "41.33.10.10", at, DemoOs(deviceId));
            a.Heartbeat("2.4.1", "41.33.10.10", lastHeartbeat);
            db.LicenseActivations.Add(a);
            db.ActivationAttempts.Add(ActivationAttempt.Record(l.TenantId, l, l.ProductKeyPrefix, deviceId, null, "41.33.10.10", at));
            await db.SaveChangesAsync(ct);
            await db.TryReserveActivationSlotAsync(l.Id, ct);
        }
    }
}

public static class DbInitializerExtensions
{
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync(ct);
    }
}
