using FluentValidation;
using Licensing.BuildingBlocks.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Licensing.Modules.Catalog.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the Catalog module's handlers and request validators. The host also registers an
    /// <see cref="ICatalogUsageReader"/>, which lives outside the module.
    /// </summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        services.AddHandlersFrom(assembly);
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        return services;
    }
}
