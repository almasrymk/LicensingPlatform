using FluentValidation;
using Licensing.Application.Audit;
using Licensing.Application.Catalog;
using Licensing.Application.Customers;
using Licensing.Application.Events;
using Licensing.Application.Identity;
using Licensing.Application.Integrations;
using Licensing.Application.Licensing;
using Licensing.Application.Reports;
using Licensing.Application.Subscriptions;
using Licensing.Application.Tenants;
using Licensing.Domain.Licensing;
using Licensing.Domain.Subscriptions;
using Microsoft.Extensions.DependencyInjection;

namespace Licensing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<TenantService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<LicenseService>();
        services.AddScoped<DeviceLicensingService>();
        services.AddScoped<IntegrationService>();
        services.AddScoped<ApiClientService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<ReportService>();
        services.AddScoped<Media.MediaService>();

        services.AddScoped<IIntegrationEventHandler<SubscriptionExpiredV1>, ExpireLicensesOnSubscriptionExpired>();
        services.AddScoped<IIntegrationEventHandler<SubscriptionCancelledV1>, ExpireLicensesOnSubscriptionExpired>();
        services.AddScoped<IIntegrationEventHandler<LicenseStatusChangedV1>, InvalidateLicenseCache>();

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        return services;
    }
}
