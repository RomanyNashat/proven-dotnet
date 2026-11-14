using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>
/// Every unexpected error becomes a 500 with no detail. On .NET 10 a handler that returns true stops the
/// middleware's own error log, so the handler logs the exception itself.
/// </summary>
public sealed partial class ErrorHandler(IProblemDetailsService problemDetails, ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        LogUnhandled(logger, exception, http.Request.Method, http.Request.Path, http.TraceIdentifier);
        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." },
        });
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Method} {Path} ({TraceId})")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path, string traceId);
}
