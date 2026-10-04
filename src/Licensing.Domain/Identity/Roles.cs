namespace Licensing.Domain.Identity;

public static class Roles
{
    /// <summary>Operates the whole platform and sees every tenant, through the explicit PlatformAdmin scope only.</summary>
    public const string PlatformAdmin = "PlatformAdmin";
    /// <summary>Manages everything inside one tenant.</summary>
    public const string TenantAdmin = "TenantAdmin";
    /// <summary>Day-to-day operator inside one tenant (no user or API client management).</summary>
    public const string TenantOperator = "TenantOperator";
    /// <summary>A customer of a tenant: read-only access to its own subscriptions, licenses, devices and reports.</summary>
    public const string CustomerUser = "CustomerUser";

    public static readonly string[] All = [PlatformAdmin, TenantAdmin, TenantOperator, CustomerUser];
}

public static class Permissions
{
    public const string TenantsRead = "tenants.read";
    public const string TenantsManage = "tenants.manage";
    public const string CustomersRead = "customers.read";
    public const string CustomersManage = "customers.manage";
    public const string CatalogRead = "catalog.read";
    public const string CatalogManage = "catalog.manage";
    public const string SubscriptionsRead = "subscriptions.read";
    public const string SubscriptionsManage = "subscriptions.manage";
    public const string LicensesRead = "licenses.read";
    public const string LicensesManage = "licenses.manage";
    public const string ApiClientsManage = "apiclients.manage";
    public const string UsersManage = "users.manage";
    public const string AuditRead = "audit.read";
    public const string ReportsRead = "reports.read";
    public const string SigningKeysManage = "signingkeys.manage";

    public static readonly string[] All =
    [
        TenantsRead, TenantsManage, CustomersRead, CustomersManage, CatalogRead, CatalogManage,
        SubscriptionsRead, SubscriptionsManage, LicensesRead, LicensesManage, ApiClientsManage,
        UsersManage, AuditRead, ReportsRead, SigningKeysManage,
    ];

    private static readonly Dictionary<string, string[]> ByRole = new()
    {
        [Roles.PlatformAdmin] = All,
        [Roles.TenantAdmin] =
        [
            CustomersRead, CustomersManage, CatalogRead, CatalogManage, SubscriptionsRead, SubscriptionsManage,
            LicensesRead, LicensesManage, ApiClientsManage, UsersManage, AuditRead, ReportsRead,
        ],
        [Roles.TenantOperator] =
        [
            CustomersRead, CustomersManage, CatalogRead, SubscriptionsRead, SubscriptionsManage,
            LicensesRead, LicensesManage, ReportsRead,
        ],
        [Roles.CustomerUser] = [CustomersRead, CatalogRead, SubscriptionsRead, LicensesRead, ReportsRead],
    };

    public static IReadOnlyList<string> ForRole(string role) => ByRole.TryGetValue(role, out var p) ? p : [];
}
