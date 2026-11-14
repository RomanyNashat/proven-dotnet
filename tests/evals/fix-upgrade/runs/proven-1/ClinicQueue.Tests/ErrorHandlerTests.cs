using ClinicQueue.Application;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ClinicQueue.Tests;

public sealed class ErrorHandlerTests
{
    [Fact]
    public async Task TryHandleAsync_UnexpectedException_LogsItWithTheException()
    {
        var logger = new FakeLogger<ErrorHandler>();
        var handler = new ErrorHandler(ProblemDetailsService(), logger);
        var exception = new InvalidOperationException("boom");

        await handler.TryHandleAsync(NewContext(), exception, CancellationToken.None);

        logger.Collector.LatestRecord.Level.Should().Be(LogLevel.Error);
        logger.Collector.LatestRecord.Exception.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task TryHandleAsync_UnexpectedException_Answers500()
    {
        var handler = new ErrorHandler(ProblemDetailsService(), new FakeLogger<ErrorHandler>());
        var http = NewContext();

        var handled = await handler.TryHandleAsync(http, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task TryHandleAsync_ClientAcceptsOnlyHtml_StillAnswers500()
    {
        var handler = new ErrorHandler(ProblemDetailsService(), new FakeLogger<ErrorHandler>());
        var http = NewContext();
        http.Request.Headers.Accept = "text/html";

        var handled = await handler.TryHandleAsync(http, new InvalidOperationException("boom"), CancellationToken.None);

        handled.Should().BeTrue();
        http.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    private static IProblemDetailsService ProblemDetailsService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddProblemDetails();
        return services.BuildServiceProvider().GetRequiredService<IProblemDetailsService>();
    }

    private static DefaultHttpContext NewContext()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.Request.Headers.Accept = "application/json";
        return http;
    }
}
