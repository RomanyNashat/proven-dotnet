using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace ClinicQueue.Application;

/// <summary>Leaves unexpected errors to the exception handler middleware: by returning false the error is
/// written as a problem (AddProblemDetails) and logged, which on .NET 10 a handler that returns true stops.</summary>
public sealed class ErrorHandler : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct) => ValueTask.FromResult(false);
}
