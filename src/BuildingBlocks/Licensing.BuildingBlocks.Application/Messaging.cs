namespace Licensing.BuildingBlocks.Application;

/// <summary>A request that may change state. Each command type has exactly one handler.</summary>
public interface ICommand<TResult>;

/// <summary>A request that only reads. Query handlers never save changes.</summary>
public interface IQuery<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}

/// <summary>Sends commands and queries to their handler through the pipeline behaviors.</summary>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken ct = default);
    Task<TResult> QueryAsync<TResult>(IQuery<TResult> query, CancellationToken ct = default);
}

public delegate Task<TResult> HandlerDelegate<TResult>();

/// <summary>
/// Wraps every command and query. Behaviors run in registration order, the first registered being the outermost.
/// </summary>
public interface IPipelineBehavior<in TRequest, TResult> where TRequest : notnull
{
    Task<TResult> HandleAsync(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct);
}
