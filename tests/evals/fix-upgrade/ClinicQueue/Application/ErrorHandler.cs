using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ClinicQueue.Application;

/// <summary>Every unexpected error becomes a 500 with no detail; the middleware logs it.</summary>
public sealed class ErrorHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = 500, Title = "An unexpected error occurred." },
        });
        return true;
    }
}
