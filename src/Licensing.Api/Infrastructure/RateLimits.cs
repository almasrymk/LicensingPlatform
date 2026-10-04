using System.Threading.RateLimiting;
using Licensing.Infrastructure.Security;
using Licensing.SharedKernel;
using Microsoft.AspNetCore.RateLimiting;

namespace Licensing.Api.Infrastructure;

public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    public int LoginPerMinute { get; set; } = 10;
    public int ClientTokenPerMinute { get; set; } = 30;
    public int LicensingPerMinute { get; set; } = 300;
}

/// <summary>
/// Rate limits of plan section 28. Partitioned per IP for anonymous endpoints and per API client for licensing.
/// In-process counters; a multi-instance deployment moves them to Redis (see docs/operations.md).
/// </summary>
public static class RateLimits
{
    public const string Login = "login";
    public const string ClientToken = "client-token";
    public const string Licensing = "licensing";

    private static RateLimitOptions Limits(HttpContext http) =>
        http.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<RateLimitOptions>>().Value;

    public static IServiceCollection AddLicensingRateLimits(this IServiceCollection services)
    {
        services.AddOptions<RateLimitOptions>().BindConfiguration(RateLimitOptions.Section);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                var problem = ApiResults.Problem(ctx.HttpContext,
                    new Error("RATE_LIMITED", "Too many requests. Slow down and retry later.", ErrorKind.TooManyRequests));
                await ctx.HttpContext.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
            };

            options.AddPolicy(Login, http => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Limits(http).LoginPerMinute, Window = TimeSpan.FromMinutes(1) }));

            options.AddPolicy(ClientToken, http => RateLimitPartition.GetFixedWindowLimiter(
                "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Limits(http).ClientTokenPerMinute, Window = TimeSpan.FromMinutes(1) }));

            options.AddPolicy(Licensing, http => RateLimitPartition.GetSlidingWindowLimiter(
                "client:" + (http.User.FindFirst(ClaimNames.ClientId)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = Limits(http).LicensingPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6,
                }));
        });
        return services;
    }
}
