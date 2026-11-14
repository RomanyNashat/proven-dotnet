using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>
/// Every unexpected error becomes a 500 with no detail. It logs the exception itself: from .NET 10, a handler
/// that returns true means the middleware writes no diagnostics, so without this the error never reached the logs.
/// </summary>
public sealed partial class ErrorHandler(IProblemDetailsService problemDetails, ILogger<ErrorHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        LogUnhandled(logger, exception, http.Request.Method, http.Request.Path);

        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        // TryWriteAsync: WriteAsync throws when no writer accepts the request's Accept header, and the client
        // then gets an empty 500 from the middleware instead of this one.
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." },
            Exception = exception,
        });
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);
}
