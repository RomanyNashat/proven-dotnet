using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MeterSync.Api;

// For bugs and outages only; expected failures are results mapped in the endpoint. The response says nothing
// about the exception, and neither does the log beyond its type: messages can carry patient data.
public sealed class UnexpectedExceptionHandler(IProblemDetailsService problemDetails, ILogger<UnexpectedExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            return true;   // the client went away
        }

        // The type and stack trace, not the message: messages can echo values (a national ID, a reading).
        logger.LogError("Unhandled {ExceptionType} on {Method} {Route}, trace {TraceId}\n{StackTrace}",
            exception.GetType().FullName, http.Request.Method, http.GetEndpoint()?.DisplayName, http.TraceIdentifier, exception.StackTrace);

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
