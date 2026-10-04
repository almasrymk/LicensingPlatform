using System.Diagnostics;
using Licensing.SharedKernel;
using Microsoft.AspNetCore.Mvc;

namespace Licensing.Api.Infrastructure;

/// <summary>Maps application results to HTTP. Every error body is RFC 7807 ProblemDetails with a stable <c>code</c> and the <c>traceId</c>.</summary>
public static class ApiResults
{
    public static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Locked => StatusCodes.Status423Locked,
        ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        ErrorKind.Gone => StatusCodes.Status410Gone,
        _ => StatusCodes.Status400BadRequest,
    };

    public static ProblemDetails Problem(HttpContext http, Error error, IDictionary<string, string[]>? errors = null)
    {
        var status = StatusFor(error.Kind);
        var problem = errors is null
            ? new ProblemDetails()
            : new ValidationProblemDetails(errors);
        problem.Status = status;
        problem.Title = error.Message;
        problem.Type = $"https://httpstatuses.io/{status}";
        problem.Instance = http.Request.Path;
        problem.Extensions["code"] = error.Code;
        problem.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;
        return problem;
    }

    public static ObjectResult ToProblem(this ControllerBase c, Error error) =>
        new(Problem(c.HttpContext, error)) { StatusCode = StatusFor(error.Kind), ContentTypes = { "application/problem+json" } };

    public static IActionResult ToActionResult<T>(this ControllerBase c, Result<T> result) =>
        result.IsSuccess ? c.Ok(result.Value) : c.ToProblem(result.Error!);

    public static IActionResult ToActionResult(this ControllerBase c, Result result) =>
        result.IsSuccess ? c.NoContent() : c.ToProblem(result.Error!);

    public static IActionResult ToCreated<T>(this ControllerBase c, Result<T> result, Func<T, object> id) =>
        result.IsSuccess ? c.StatusCode(StatusCodes.Status201Created, result.Value) : c.ToProblem(result.Error!);
}
