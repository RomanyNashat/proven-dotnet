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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(ex, "Request failed after the response started");
                throw;
            }

            var (status, message) = ex switch
            {
                RequestRejectedException => (StatusCodes.Status400BadRequest, ex.Message),
                UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
                _ => (StatusCodes.Status500InternalServerError, "Something went wrong"),
            };
            if (status == StatusCodes.Status500InternalServerError)
            {
                logger.LogError(ex, "Request failed");
            }

            context.Response.Clear();
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new { error = message });
        }
    }
}
