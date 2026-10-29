using System.Globalization;
using System.Threading.RateLimiting;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LabResults.Api;

/// <summary>
/// One bucket per caller (<c>sub</c>), per pod. Recording a result sends a paid SMS; a lookup reads another
/// system's patient data. Limits come from <c>RateLimits:RecordPerMinute</c> / <c>RateLimits:LookupPerMinute</c>.
/// </summary>
public static class PerCallerRateLimits
{
    public const string Record = "record";
    public const string Lookup = "lookup";

    public static IServiceCollection AddPerCallerRateLimits(this IServiceCollection services, IConfiguration config) =>
        services.AddRateLimiter(o =>
        {
            o.AddPolicy(Record, http => PerMinute(http, config.GetValue("RateLimits:RecordPerMinute", 60)));
            o.AddPolicy(Lookup, http => PerMinute(http, config.GetValue("RateLimits:LookupPerMinute", 60)));
            o.OnRejected = async (ctx, ct) =>
            {
                var http = ctx.HttpContext;
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Title = "Too many requests." },
                });
            };
        });

    private static RateLimitPartition<string> PerMinute(HttpContext http, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(
            CurrentUser.IdOf(http.User) is { } sub ? $"sub:{sub}" : $"ip:{http.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) });
}
