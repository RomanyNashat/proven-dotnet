using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ClinicReminders.Api;

// For bugs and outages only. The response says nothing about the exception, and neither does the log
// beyond its type: an exception's message can carry patient data. The traceId finds the request.
public sealed partial class UnexpectedExceptionHandler(IProblemDetailsService problemDetails, ILogger<UnexpectedExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            return true;   // the client went away: nothing to answer, nothing to alert on
        }

        LogUnhandled(logger, exception.GetType().Name, http.Request.Method, http.GetEndpoint()?.DisplayName, http.TraceIdentifier);

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled {ExceptionType} on {Method} {Route} (trace {TraceId})")]
    private static partial void LogUnhandled(ILogger logger, string exceptionType, string method, string? route, string traceId);
}
