using ClinicQueue.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Tests;

public sealed class ErrorHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_UnexpectedException_LogsErrorWithException()
    {
        var logger = new CapturingLogger<ErrorHandler>();
        var handler = new ErrorHandler(new NoOpProblemDetailsService(), logger);
        var exception = new InvalidOperationException("boom");

        await handler.TryHandleAsync(new DefaultHttpContext(), exception, CancellationToken.None);

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception == exception);
    }

    [Fact]
    public async Task TryHandleAsync_UnexpectedException_Answers500()
    {
        var http = new DefaultHttpContext();
        var handler = new ErrorHandler(new NoOpProblemDetailsService(), new CapturingLogger<ErrorHandler>());

        var handled = await handler.TryHandleAsync(http, new InvalidOperationException("boom"), CancellationToken.None);

        (handled, http.Response.StatusCode).Should().Be((true, StatusCodes.Status500InternalServerError));
    }

    [Fact]
    public async Task TryHandleAsync_ClientAborted_NoErrorLogged()
    {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var http = new DefaultHttpContext { RequestAborted = aborted.Token };
        var logger = new CapturingLogger<ErrorHandler>();
        var handler = new ErrorHandler(new NoOpProblemDetailsService(), logger);

        await handler.TryHandleAsync(http, new OperationCanceledException(aborted.Token), CancellationToken.None);

        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);
    }

    private sealed class NoOpProblemDetailsService : IProblemDetailsService
    {
        public ValueTask WriteAsync(ProblemDetailsContext context) => ValueTask.CompletedTask;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }
}
