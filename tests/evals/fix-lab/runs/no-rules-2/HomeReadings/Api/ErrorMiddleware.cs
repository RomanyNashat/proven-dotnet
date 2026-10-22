using HomeReadings.Application;

namespace HomeReadings.Api;

/// <summary>Turns an unhandled error into a JSON body the app can show, without leaking internals.</summary>
public sealed class ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
        {
            if (ex is InvalidReadingException invalid)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = invalid.Message });
            }
            else if (ex is UnauthorizedAccessException)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden" });
            }
            else
            {
                logger.LogError(ex, "Request failed");
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await context.Response.WriteAsJsonAsync(new { error = "Something went wrong" });
            }
        }
    }
}
