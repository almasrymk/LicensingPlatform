using System.Net;
using Licensing.Domain.Licensing;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Licensing.Tests.Api;

/// <summary>
/// E3: the DeviceCountersFromActivations migration on a synthetic database. It must replace the old SUM backfill:
/// one count per distinct device, nothing from revoked or expired licenses or from deactivated seats.
/// </summary>
public sealed class DeviceCounterMigrationTests : IClassFixture<TestAppFixture>
{
    private const string BeforeFix = "20261104165322_SuspensionOrigin";

    private readonly TestApp _app;
    private readonly LicenseSetup _setup;

    public DeviceCounterMigrationTests(TestAppFixture fixture)
    {
        _app = fixture.App;
        _setup = new LicenseSetup(_app);
    }

    [SqlServerFact]
    public async Task The_backfill_counts_distinct_valid_devices_only()
    {
        var issued = await _setup.IssueAsync(maxActivations: 10);
        var admin = issued.Admin;
        async Task<(Guid Id, string Key)> AnotherAsync()
        {
            var created = await (await admin.PostJsonAsync("/api/v1/licenses", new { subscriptionId = issued.SubscriptionId })).OkJsonAsync(HttpStatusCode.Created);
            return (created.GetProperty("license").GetProperty("id").GetGuid(), created.GetProperty("productKey").GetString()!);
        }
        var shared = await AnotherAsync();
        var revoked = await AnotherAsync();
        var expired = await AnotherAsync();
        var cloud = await _app.ClientTokenAsync(DemoAccounts.NourClientId, DemoAccounts.NourClientSecret);
        async Task ActivateAsync(string key, string device) =>
            await (await cloud.PostJsonAsync("/api/v1/licensing/activate", new { productKey = key, deviceId = device, productCode = issued.ProductCode })).OkJsonAsync();

        await ActivateAsync(issued.Key, "MIGRATE-DEVICE-01");
        await ActivateAsync(issued.Key, "MIGRATE-DEVICE-02");
        await ActivateAsync(shared.Key, "MIGRATE-DEVICE-01");   // the same fingerprint on a second license
        await ActivateAsync(shared.Key, "MIGRATE-DEVICE-03");
        await ActivateAsync(revoked.Key, "MIGRATE-DEVICE-04");
        await ActivateAsync(expired.Key, "MIGRATE-DEVICE-05");
        Assert.Equal(HttpStatusCode.NoContent, (await cloud.PostJsonAsync($"/api/v1/integration/licenses/{shared.Id}/devices/MIGRATE-DEVICE-03/release", new { })).StatusCode);

        // The state before E3: a revoked license that kept its seats, an expired one, and the SUM counters.
        await _app.WithDbAsync(async db =>
        {
            await db.Licenses.IgnoreQueryFilters().Where(l => l.Id == revoked.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, LicenseStatus.Revoked));
            await db.Licenses.IgnoreQueryFilters().Where(l => l.Id == expired.Id).ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, LicenseStatus.Expired));
            await db.Licenses.IgnoreQueryFilters().Where(l => l.SubscriptionId == issued.SubscriptionId).ExecuteUpdateAsync(s => s.SetProperty(l => l.ActiveActivations, 9));
            return await db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == issued.SubscriptionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ActiveDevices, 36));
        });

        // Down is a no-op for the data, so going back one step and forward again runs the corrective Up on this data.
        await _app.WithDbAsync(async db =>
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeFix);
            await migrator.MigrateAsync();
            return 0;
        });

        Assert.Equal(2, await _app.WithDbAsync(db => db.Subscriptions.IgnoreQueryFilters()   // 01 and 02; 01 once
            .Where(s => s.Id == issued.SubscriptionId).Select(s => s.ActiveDevices).FirstAsync()));
        var seats = await _app.WithDbAsync(db => db.Licenses.IgnoreQueryFilters().Where(l => l.SubscriptionId == issued.SubscriptionId)
            .ToDictionaryAsync(l => l.Id, l => l.ActiveActivations));
        Assert.Equal(2, seats[issued.LicenseId]);
        Assert.Equal(1, seats[shared.Id]);
        Assert.Equal(1, seats[revoked.Id]);   // the seat row is untouched; the subscription just does not count it
        Assert.Equal(1, seats[expired.Id]);
        Assert.Equal(0, await _app.WithDbAsync(db => db.ReconcileDeviceCountersAsync(500, CancellationToken.None)));
    }
}
