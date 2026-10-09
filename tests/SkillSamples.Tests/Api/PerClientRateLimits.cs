using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Api;

public static class PerClientRateLimits
{
    public const string Otp = "otp";

    // One bucket per caller: the user id when signed in, otherwise the client IP (which is only the real
    // client behind nginx when ForwardedHeaders is set up; see the nginx skill). A limiter added without
    // a partition is ONE bucket shared by every caller of the endpoint.
    // These buckets live in each pod. For a limit that must hold across pods (OTP abuse, a paid
    // downstream), use the Redis limiter in redis-patterns.
    public static IServiceCollection AddPerClientRateLimits(this IServiceCollection services, int otpPerWindow = 5) =>
        services.AddRateLimiter(o =>
        {
            o.AddPolicy(Otp, http => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = otpPerWindow, Window = TimeSpan.FromMinutes(1) }));

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

    private static string PartitionKey(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) is { } userId
            ? $"user:{userId}"
            : $"ip:{http.Connection.RemoteIpAddress}";
}
