using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LabResults.Api;

/// <summary>
/// Bugs and outages only. The response says nothing about the exception; the traceId finds it in the logs.
/// The log line carries the type, not the exception: a downstream error's text can repeat patient data.
/// </summary>
public sealed class UnexpectedExceptionHandler(IProblemDetailsService problemDetails, ILogger<UnexpectedExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            return true;   // the client went away
        }

        // The path, not the query string. No route of this service carries patient data in its path.
        logger.LogError("Unhandled {ExceptionType} on {Method} {Path}, trace {TraceId}",
            exception.GetType().FullName, http.Request.Method, http.Request.Path.Value, http.TraceIdentifier);

        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Extensions = { ["traceId"] = http.TraceIdentifier },
            },
        });
    }
}
