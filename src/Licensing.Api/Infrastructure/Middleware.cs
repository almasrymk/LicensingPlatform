using FluentValidation;
using Licensing.SharedKernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Serilog.Context;

namespace Licensing.Api.Infrastructure;

/// <summary>Turns exceptions into ProblemDetails. Domain rule violations keep their code; anything else is a generic 500.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        Error error;
        if (exception is DomainException domain)
        {
            error = domain.Error;
        }
        else if (exception is BadHttpRequestException)
        {
            error = Error.Validation("BAD_REQUEST", "The request could not be read.");
        }
        else
        {
            logger.LogError(exception, "Unhandled exception");
            error = new Error("INTERNAL_ERROR", "An unexpected error occurred. Quote the traceId when contacting support.", ErrorKind.Validation);
            var p500 = ApiResults.Problem(http, error);
            p500.Status = StatusCodes.Status500InternalServerError;
            p500.Type = "https://httpstatuses.io/500";
            http.Response.StatusCode = 500;
            await http.Response.WriteAsJsonAsync(p500, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
            return true;
        }

        var problem = ApiResults.Problem(http, error);
        http.Response.StatusCode = problem.Status!.Value;
        await http.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        return true;
    }
}

/// <summary>Runs FluentValidation validators for every action argument that has one (the validation pipeline).</summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null) continue;
            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (services.GetService(validatorType) is not IValidator validator) continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid) continue;

            var errors = result.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            var problem = ApiResults.Problem(context.HttpContext, Error.Validation("VALIDATION_FAILED", "One or more fields are invalid."), errors);
            context.Result = new ObjectResult(problem) { StatusCode = 400, ContentTypes = { "application/problem+json" } };
            return;
        }
        await next();
    }
}

/// <summary>Accepts or creates X-Correlation-Id and pushes it to the log context and the response.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext http)
    {
        var incoming = http.Request.Headers[Header].FirstOrDefault();
        var id = !string.IsNullOrWhiteSpace(incoming) && incoming.Length <= 64 && incoming.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? incoming
            : Guid.NewGuid().ToString("N");
        http.TraceIdentifier = id;
        http.Response.Headers[Header] = id;
        using (LogContext.PushProperty("CorrelationId", id))
            await next(http);
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var h = http.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        if (http.Request.Path.StartsWithSegments("/api"))
            h["Cache-Control"] = "no-store";
        return next(http);
    }
}
