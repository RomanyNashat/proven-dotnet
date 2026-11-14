using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>
/// Every unexpected error becomes a 500 with no detail. It logs the error itself: from .NET 10 a handler
/// that returns true stops the middleware's own error log.
/// </summary>
public sealed partial class ErrorHandler(IProblemDetailsService problemDetails, ILogger<ErrorHandler> logger) : IExceptionHandler
{
    private const int ClientClosedRequest = 499;

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        // A screen that hangs up mid-request isn't a server fault; at Error level it would page someone.
        if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            LogAborted(logger, http.Request.Method, http.Request.Path);
            http.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        LogUnhandled(logger, exception, http.Request.Method, http.Request.Path);
        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." },
        });
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, PathString path);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client closed the request on {Method} {Path}")]
    private static partial void LogAborted(ILogger logger, string method, PathString path);
}
