using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Licensing.BuildingBlocks.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the dispatcher and the pipeline (logging outermost, then validation). Safe to call from every module.
    /// </summary>
    public static IServiceCollection AddCqrs(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IDispatcher))) return services;
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        return services;
    }

    /// <summary>
    /// Registers every command and query handler in <paramref name="assembly"/>. Fails at startup when a command or
    /// query has more than one handler, so a duplicate can never be picked silently at runtime.
    /// </summary>
    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        services.AddCqrs();
        foreach (var (service, implementation) in FindHandlers(assembly))
        {
            if (services.Any(d => d.ServiceType == service))
                throw new InvalidOperationException($"{service.GenericTypeArguments[0].Name} already has a handler; {implementation.Name} is a second one.");
            services.AddScoped(service, implementation);
        }
        return services;
    }

    internal static IEnumerable<(Type Service, Type Implementation)> FindHandlers(Assembly assembly) =>
        from type in assembly.GetTypes()
        where type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
        from service in type.GetInterfaces()
        where service.IsGenericType && IsHandlerInterface(service.GetGenericTypeDefinition())
        select (service, type);

    internal static bool IsHandlerInterface(Type openGeneric) =>
        openGeneric == typeof(ICommandHandler<,>) || openGeneric == typeof(IQueryHandler<,>);
}
