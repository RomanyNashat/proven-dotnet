using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClinicQueue.Tests;

public sealed class ErrorHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_UnexpectedException_LogsItAsError()
    {
        var logger = new ListLogger<ErrorHandler>();
        var handler = new ErrorHandler(new NullProblemDetailsService(), logger);
        var boom = new InvalidOperationException("boom");

        await handler.TryHandleAsync(new DefaultHttpContext(), boom, CancellationToken.None);

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception == boom);
    }

    [Fact]
    public async Task TryHandleAsync_UnexpectedException_Returns500WithoutDetail()
    {
        var problems = new NullProblemDetailsService();
        var handler = new ErrorHandler(problems, new ListLogger<ErrorHandler>());
        var http = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(http, new InvalidOperationException("secret detail"), CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(500);
        problems.Written!.ProblemDetails.Detail.Should().BeNull();
    }

    private sealed class NullProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }
}
