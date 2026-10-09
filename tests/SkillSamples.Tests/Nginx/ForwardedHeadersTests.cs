using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace SkillSamples.Nginx;

// In-process: what the app sees as the client, behind ingress -> nginx -> app in a pod network.
public sealed class ForwardedHeadersTests
{
    private const string PodCidr = "10.42.0.0/16";
    private static readonly IPAddress NginxPod = IPAddress.Parse("10.42.0.7");

    private static async Task<(string Ip, string Scheme)> SeenByApp(ForwardedHeadersOptions options, string forwardedFor, string proto = "https")
    {
        string ip = "", scheme = "";
        var middleware = new ForwardedHeadersMiddleware(
            ctx =>
            {
                ip = ctx.Connection.RemoteIpAddress!.ToString();
                scheme = ctx.Request.Scheme;
                return Task.CompletedTask;
            },
            NullLoggerFactory.Instance, Options.Create(options));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = NginxPod;
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        context.Request.Headers["X-Forwarded-Proto"] = proto;
        await middleware.Invoke(context);
        return (ip, scheme);
    }

    private static ForwardedHeadersOptions Configured()
    {
        var builder = WebApplication.CreateBuilder();
        builder.ConfigureBehindNginx(PodCidr);
        using var app = builder.Build();
        return app.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
    }

    [Fact]
    public async Task Defaults_BehindAProxyPod_TheAppSeesTheProxyAndHttp()
    {
        var defaults = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };

        var (ip, scheme) = await SeenByApp(defaults, "203.0.113.9, 10.42.1.5");

        Assert.Equal("10.42.0.7", ip);    // every request "comes from" nginx: rate limits and audit logs are wrong
        Assert.Equal("http", scheme);     // and generated links (OIDC callbacks, Location headers) say http
    }

    [Fact]
    public async Task Configured_TwoHops_TheAppSeesTheClientAndHttps()
    {
        var (ip, scheme) = await SeenByApp(Configured(), "203.0.113.9, 10.42.1.5");

        Assert.Equal("203.0.113.9", ip);
        Assert.Equal("https", scheme);
    }

    [Fact]
    public async Task Configured_AClientWritingItsOwnForwardedFor_IsNotBelieved()
    {
        // The client sent "X-Forwarded-For: 1.2.3.4"; the ingress appended the real client, nginx the ingress.
        var (ip, _) = await SeenByApp(Configured(), "1.2.3.4, 203.0.113.9, 10.42.1.5");

        Assert.Equal("203.0.113.9", ip);
    }
}
