using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Licensing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// E3 (R1/R2): rewrites the device counters from the activations. The SubscriptionActiveDevices backfill summed the
    /// licenses' counters, which counted revoked and expired licenses and counted a device once per license it held.
    /// Here a subscription counts each device once (DISTINCT DeviceId), and only Active activations (Status 1) of licenses
    /// that are neither revoked (3) nor expired (4). No activation is changed; only the cached counters are.
    /// </summary>
    public partial class DeviceCountersFromActivations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE l SET ActiveActivations = (
                    SELECT COUNT(*) FROM licensing.LicenseActivations a WHERE a.LicenseId = l.Id AND a.Status = 1)
                FROM licensing.Licenses l;
                """);
            migrationBuilder.Sql("""
                UPDATE s SET ActiveDevices = (
                    SELECT COUNT(DISTINCT a.DeviceId)
                    FROM licensing.LicenseActivations a
                    JOIN licensing.Licenses l ON l.Id = a.LicenseId
                    WHERE l.SubscriptionId = s.Id AND a.Status = 1 AND l.Status NOT IN (3, 4))
                FROM subscriptions.Subscriptions s;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Counters only: the previous values were wrong, and the reconciliation job keeps them correct either way.
        }
    }
}
