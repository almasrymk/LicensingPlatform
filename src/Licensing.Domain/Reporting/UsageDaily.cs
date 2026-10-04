using Licensing.SharedKernel;

namespace Licensing.Domain.Reporting;

/// <summary>Daily usage read model per customer, written by the usage aggregation job; never touched by the aggregates.</summary>
public sealed class UsageDaily : Entity, ICustomerOwned
{
    private UsageDaily() { }

    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public DateOnly Day { get; private set; }
    public int ActiveLicenses { get; private set; }
    public int ActiveDevices { get; private set; }
    public int SuccessfulActivations { get; private set; }
    public int FailedActivations { get; private set; }

    public static UsageDaily Create(Guid tenantId, Guid customerId, DateOnly day) => new() { TenantId = tenantId, CustomerId = customerId, Day = day };

    public void Set(int activeLicenses, int activeDevices, int successfulActivations, int failedActivations)
    {
        ActiveLicenses = activeLicenses;
        ActiveDevices = activeDevices;
        SuccessfulActivations = successfulActivations;
        FailedActivations = failedActivations;
    }
}
