using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HomeReadings.Api;

/// <summary>
/// For bugs and outages only. The response says nothing about the exception: its message can carry
/// patient data or database internals. The traceId is how support finds the full error in the logs.
/// </summary>
public sealed class UnexpectedExceptionHandler(IProblemDetailsService problemDetails, ILogger<UnexpectedExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
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
