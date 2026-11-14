using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Application;

/// <summary>Every unexpected error becomes a 500 with no detail. It logs the error itself: since .NET 10 the
/// middleware writes no diagnostics for an exception a handler says it handled.</summary>
public sealed class ErrorHandler(IProblemDetailsService problemDetails, ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        logger.LogError(exception, "Unhandled {ExceptionType} on {Method} {Path}", exception.GetType().Name, http.Request.Method, http.Request.Path);
        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." },
        });
        return true;
    }
}
