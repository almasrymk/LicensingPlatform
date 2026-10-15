using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Licensing.BuildingBlocks.Application;

/// <summary>
/// Resolves the single handler of a command or query and runs it inside the registered pipeline behaviors.
/// The closed wrapper for each request type is built once and cached, so dispatch does no reflection after the first call.
/// </summary>
internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<(Type Request, Type Result), object> Wrappers = new();

    public Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Wrapper<TResult>(command.GetType(), typeof(CommandWrapper<,>)).HandleAsync(command, services, ct);
    }

    public Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Wrapper<TResult>(query.GetType(), typeof(QueryWrapper<,>)).HandleAsync(query, services, ct);
    }

    private static RequestWrapper<TResult> Wrapper<TResult>(Type requestType, Type openWrapper) =>
        (RequestWrapper<TResult>)Wrappers.GetOrAdd((requestType, typeof(TResult)),
            key => Activator.CreateInstance(openWrapper.MakeGenericType(key.Request, key.Result))!);
}

internal abstract class RequestWrapper<TResult>
{
    public abstract Task<TResult> HandleAsync(object request, IServiceProvider services, CancellationToken ct);

    /// <summary>Behaviors are resolved in registration order; the first registered becomes the outermost.</summary>
    protected static Task<TResult> RunPipeline<TRequest>(TRequest request, IServiceProvider services, HandlerDelegate<TResult> handler,
        CancellationToken ct) where TRequest : notnull
    {
        var next = handler;
        foreach (var behavior in services.GetServices<IPipelineBehavior<TRequest, TResult>>().Reverse())
        {
            var inner = next;
            next = () => behavior.HandleAsync(request, inner, ct);
        }
        return next();
    }
}

internal sealed class CommandWrapper<TCommand, TResult> : RequestWrapper<TResult> where TCommand : ICommand<TResult>
{
    public override Task<TResult> HandleAsync(object request, IServiceProvider services, CancellationToken ct)
    {
        var command = (TCommand)request;
        var handler = services.GetService<ICommandHandler<TCommand, TResult>>()
            ?? throw new InvalidOperationException($"No handler is registered for command {typeof(TCommand).Name}.");
        return RunPipeline(command, services, () => handler.HandleAsync(command, ct), ct);
    }
}

internal sealed class QueryWrapper<TQuery, TResult> : RequestWrapper<TResult> where TQuery : IQuery<TResult>
{
    public override Task<TResult> HandleAsync(object request, IServiceProvider services, CancellationToken ct)
    {
        var query = (TQuery)request;
        var handler = services.GetService<IQueryHandler<TQuery, TResult>>()
            ?? throw new InvalidOperationException($"No handler is registered for query {typeof(TQuery).Name}.");
        return RunPipeline(query, services, () => handler.HandleAsync(query, ct), ct);
    }
}
