using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>Every unexpected error becomes a 500 with no detail, logged here.</summary>
public sealed partial class ErrorHandler(IProblemDetailsService problemDetails, ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        // From .NET 10 the exception handler middleware no longer logs an exception that a handler
        // reports as handled, so the log line that used to come from the middleware comes from here.
        LogUnhandled(logger, exception, http.Request.Method, http.Request.Path, http.TraceIdentifier);

        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." },
        });
        return true;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unhandled exception on {Method} {Path} ({TraceId})")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path, string traceId);
}
