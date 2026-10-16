using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReminders.Api;

// For bugs and outages only. Expected failures (not found, not allowed, invalid) are results mapped to
// TypedResults in the endpoint, never exceptions.
// The response says nothing about the exception: its message can carry patient data or internals. The
// traceId is how support finds the full error in the logs.
public sealed class UnexpectedExceptionHandler(IProblemDetailsService problemDetails, ILogger<UnexpectedExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            return true;   // the client went away: nothing to answer, nothing to alert on
        }

        logger.LogError(exception, "Unhandled {ExceptionType} on {Method} {Route}",
            exception.GetType().Name, http.Request.Method, http.GetEndpoint()?.DisplayName);

        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Extensions = { ["traceId"] = http.TraceIdentifier },
            },
        });
    }
}
