using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReminders.Api;

public static class RateLimits
{
    public const string ScheduleReminder = "schedule-reminder";

    // Each scheduled reminder becomes an SMS, so the POST is limited per caller: the token's sub (inbound
    // claims are not mapped, so it is "sub", not NameIdentifier), otherwise the client IP. The buckets live in
    // each pod; a limit that must hold across pods needs a shared store.
    public static IServiceCollection AddReminderRateLimits(this IServiceCollection services, IConfiguration config) =>
        services.AddRateLimiter(o =>
        {
            o.AddPolicy(ScheduleReminder, http => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(http),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimits:SchedulePerMinute", 30),
                    Window = TimeSpan.FromMinutes(1),
                }));

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
        http.User.FindFirst("sub")?.Value is { } sub
            ? $"user:{sub}"
            : $"ip:{http.Connection.RemoteIpAddress}";
}
