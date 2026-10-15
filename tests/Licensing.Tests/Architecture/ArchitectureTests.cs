using System.Reflection;
using NetArchTest.Rules;

namespace Licensing.Tests.Architecture;

/// <summary>The dependency rules of plan section 5. They run on every build so a boundary violation fails CI.</summary>
public class ArchitectureTests
{
    private static readonly Assembly SharedKernel = typeof(Licensing.SharedKernel.Entity).Assembly;
    private static readonly Assembly Domain = typeof(Licensing.Domain.Tenants.Tenant).Assembly;
    private static readonly Assembly Application = typeof(Licensing.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(Licensing.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;
    private static readonly Assembly BuildingBlocks = typeof(Licensing.BuildingBlocks.Application.IDispatcher).Assembly;
    private static readonly Assembly CatalogModule = typeof(Licensing.Modules.Catalog.Application.DependencyInjection).Assembly;

    /// <summary>Every module project. Add a module here when it is split out (ADR-015).</summary>
    private static readonly Assembly[] Modules = [CatalogModule];

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
        Assert.True(result.IsSuccessful, "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact] // 1
    public void SharedKernel_depends_on_nothing_of_ours() =>
        AssertNoDependency(SharedKernel, "Licensing.Domain", "Licensing.Application", "Licensing.Infrastructure", "Licensing.Api");

    [Fact] // 2
    public void Domain_depends_only_on_SharedKernel() =>
        AssertNoDependency(Domain, "Licensing.Application", "Licensing.Infrastructure", "Licensing.Api");

    [Fact] // 3
    public void Domain_has_no_framework_dependencies() =>
        AssertNoDependency(Domain, "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "System.Net.Http");

    [Fact] // 4
    public void Application_does_not_depend_on_Infrastructure_or_Api() =>
        AssertNoDependency(Application, "Licensing.Infrastructure", "Licensing.Api");

    [Fact] // 5
    public void Infrastructure_does_not_depend_on_Api() =>
        AssertNoDependency(Infrastructure, "Licensing.Api");

    [Fact] // 6
    public void Controllers_do_not_use_the_DbContext_or_infrastructure_services_directly()
    {
        var result = Types.InAssembly(Api).That().ResideInNamespace("Licensing.Api.Controllers")
            .ShouldNot().HaveDependencyOnAny("Licensing.Infrastructure.Persistence", "Licensing.Infrastructure.Services", "Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful, "Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact] // 7
    public void Domain_entities_do_not_expose_public_setters()
    {
        var offenders = Domain.GetTypes()
            .Where(t => t.IsClass && t.Namespace?.StartsWith("Licensing.Domain") == true && !t.Name.Contains('<') && !IsRecord(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        Assert.True(offenders.Count == 0, "Public setters: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Tenant_owned_aggregates_implement_the_isolation_marker()
    {
        string[] tenantOwned = ["Customer", "CustomerContact", "Product", "Plan", "Subscription", "SubscriptionHistory", "License",
            "LicenseActivation", "ActivationAttempt", "ApiClient", "Webhook", "UsageDaily"];
        var missing = Domain.GetTypes().Where(t => tenantOwned.Contains(t.Name) && !typeof(Licensing.SharedKernel.ITenantOwned).IsAssignableFrom(t))
            .Select(t => t.Name).ToList();
        Assert.Empty(missing);
    }

    // ---------- Modules and CQRS (ADR-014, ADR-015) ----------

    [Fact]
    public void BuildingBlocks_depend_only_on_SharedKernel() =>
        AssertNoDependency(BuildingBlocks, "Licensing.Domain", "Licensing.Application", "Licensing.Infrastructure", "Licensing.Api",
            "Licensing.Modules", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore");

    [Fact]
    public void Modules_do_not_depend_on_Infrastructure_or_Api()
    {
        foreach (var module in Modules)
            AssertNoDependency(module, "Licensing.Infrastructure", "Licensing.Api");
    }

    [Fact]
    public void Shared_layers_do_not_depend_on_modules()
    {
        AssertNoDependency(SharedKernel, "Licensing.Modules");
        AssertNoDependency(Domain, "Licensing.Modules");
        AssertNoDependency(Application, "Licensing.Modules");
    }

    [Fact]
    public void Catalog_does_not_touch_other_modules_entities() =>
        AssertNoDependency(CatalogModule,
            "Licensing.Domain.Licensing", "Licensing.Domain.Subscriptions", "Licensing.Domain.Customers", "Licensing.Domain.Identity",
            "Licensing.Domain.Tenants", "Licensing.Domain.Integrations", "Licensing.Domain.Reporting", "Licensing.Domain.Audit",
            "Licensing.Application.Licensing", "Licensing.Application.Subscriptions", "Licensing.Application.Customers");

    [Fact]
    public void Every_command_and_query_in_a_module_has_exactly_one_handler()
    {
        foreach (var module in Modules)
        {
            var handled = Licensing.BuildingBlocks.Application.DependencyInjection.FindHandlers(module)
                .GroupBy(h => h.Service.GenericTypeArguments[0])
                .ToDictionary(g => g.Key, g => g.Count());
            var requests = module.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && t.GetInterfaces().Any(IsRequest)).ToList();
            Assert.NotEmpty(requests);
            var problems = requests.Where(r => handled.GetValueOrDefault(r) != 1)
                .Select(r => $"{r.Name}: {handled.GetValueOrDefault(r)} handlers").ToList();
            Assert.True(problems.Count == 0, string.Join(", ", problems));
        }
    }

    [Fact]
    public void Module_handlers_are_internal_so_callers_go_through_the_dispatcher()
    {
        foreach (var module in Modules)
        {
            var publicHandlers = Licensing.BuildingBlocks.Application.DependencyInjection.FindHandlers(module)
                .Select(h => h.Implementation).Where(t => t.IsPublic).Select(t => t.Name).Distinct().ToList();
            Assert.True(publicHandlers.Count == 0, "Public handlers: " + string.Join(", ", publicHandlers));
        }
    }

    private static bool IsRequest(Type i) => i.IsGenericType &&
        (i.GetGenericTypeDefinition() == typeof(Licensing.BuildingBlocks.Application.ICommand<>) ||
         i.GetGenericTypeDefinition() == typeof(Licensing.BuildingBlocks.Application.IQuery<>));

    private static bool IsRecord(Type t) => t.GetMethod("<Clone>$") is not null;
}
