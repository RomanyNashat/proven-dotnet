using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MeterSync.Api;

public static class RateLimits
{
    public const string LinkMeter = "link-meter";

    // One bucket per caller (the token's subject), to slow anyone trying serial numbers one after another.
    // The buckets live in each pod, so with N pods a caller gets up to N times the limit.
    public static IServiceCollection AddMeterSyncRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.AddPolicy(LinkMeter, http => RateLimitPartition.GetFixedWindowLimiter(
                $"sub:{CurrentPatient.Subject(http.User)}",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

            o.OnRejected = async (ctx, _) =>
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
}
