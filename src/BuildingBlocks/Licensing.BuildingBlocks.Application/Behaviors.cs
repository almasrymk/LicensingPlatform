using System.Diagnostics;
using System.Reflection;
using FluentValidation;
using Licensing.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Licensing.BuildingBlocks.Application;

/// <summary>Outermost behavior: logs each request's outcome and duration, and any exception it lets through.</summary>
internal sealed class LoggingBehavior<TRequest, TResult>(ILogger<LoggingBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult> where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await next();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (result is Result { IsFailure: true } failed)
                logger.LogInformation("{Request} failed with {ErrorCode} in {ElapsedMs:0} ms", name, failed.Error!.Code, elapsed);
            else
                logger.LogDebug("{Request} handled in {ElapsedMs:0} ms", name, elapsed);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Request} threw after {ElapsedMs:0} ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}

/// <summary>
/// Runs the FluentValidation validators registered for the command or query itself. A request whose result is a
/// <see cref="Result"/> gets a VALIDATION_FAILED error back; any other request type gets a <see cref="ValidationException"/>.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResult>(IServiceProvider services)
    : IPipelineBehavior<TRequest, TResult> where TRequest : notnull
{
    public async Task<TResult> HandleAsync(TRequest request, HandlerDelegate<TResult> next, CancellationToken ct)
    {
        var validators = services.GetServices<IValidator<TRequest>>().ToList();
        if (validators.Count == 0) return await next();

        var failures = new List<FluentValidation.Results.ValidationFailure>();
        foreach (var validator in validators)
            failures.AddRange((await validator.ValidateAsync(request, ct)).Errors);
        if (failures.Count == 0) return await next();

        var error = Error.Validation("VALIDATION_FAILED", string.Join(" ", failures.Select(f => f.ErrorMessage).Distinct()));
        return FailedResult.TryCreate<TResult>(error, out var result) ? result : throw new ValidationException(failures);
    }
}

internal static class FailedResult
{
    /// <summary>Builds a failed <see cref="Result"/> or <see cref="Result{T}"/> when <typeparamref name="TResult"/> is one.</summary>
    public static bool TryCreate<TResult>(Error error, out TResult result)
    {
        var type = typeof(TResult);
        if (type == typeof(Result))
        {
            result = (TResult)(object)Result.Failure(error);
            return true;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failure = type.GetMethod(nameof(Result.Failure), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, [typeof(Error)])!;
            result = (TResult)failure.Invoke(null, [error])!;
            return true;
        }
        result = default!;
        return false;
    }
}
