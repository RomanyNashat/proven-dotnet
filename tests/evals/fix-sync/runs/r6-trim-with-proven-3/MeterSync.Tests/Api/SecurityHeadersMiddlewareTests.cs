using MeterSync.Api;
using Microsoft.AspNetCore.Http;

namespace MeterSync.Tests.Api;

public sealed class SecurityHeadersMiddlewareTests
{
    [Theory]
    [InlineData("X-Content-Type-Options", "nosniff")]
    [InlineData("X-Frame-Options", "DENY")]
    [InlineData("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'")]
    [InlineData("Cache-Control", "no-store")]
    public async Task InvokeAsync_AnyRequest_SetsHeader(string header, string value)
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.Headers[header].ToString().Should().Be(value);
    }

    [Fact]
    public async Task InvokeAsync_AnyRequest_CallsNext()
    {
        var called = false;
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext());

        called.Should().BeTrue();
    }
}
