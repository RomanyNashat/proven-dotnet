using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkillSamples.Api;
using Xunit;

namespace SkillSamples.Owasp;

public sealed class OwaspTests
{
    private static async Task<WebApplication> StartAsync(Action<IServiceCollection> services, Action<WebApplication> pipeline)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
        services(builder.Services);
        var app = builder.Build();
        pipeline(app);
        await app.StartAsync();
        return app;
    }

    private static void MapStatuses(WebApplication app)
    {
        app.MapGet("/ok", () => TypedResults.Ok("fine"));
        app.MapGet("/boom", (Func<string>)(() => throw new InvalidOperationException("bug")));
        app.MapGet("/denied", () => TypedResults.Unauthorized());
        app.MapGet("/cached", (HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "public, max-age=60";
            return "reference data";
        });
    }

    private static async Task<string> HeadersOn(WebApplication app, string path)
    {
        using var response = await app.GetTestClient().GetAsync(path);
        var headers = response.Headers.Concat(response.Content.Headers).Select(h => $"{h.Key}: {string.Join(",", h.Value)}");
        return $"{(int)response.StatusCode} | {string.Join(" | ", headers)}";
    }

    // ---- A02 Security Misconfiguration: headers -------------------------------------------------------

    [Fact]
    public async Task SecurityHeaders_SetInOnStarting_AreOnEveryResponse_ErrorsIncluded()
    {
        await using var app = await StartAsync(_ => { }, a =>
        {
            a.UseApiSecurityHeaders();
            a.UseExceptionHandler();
            MapStatuses(a);
        });

        var seen = new Dictionary<string, string>();
        foreach (var path in new[] { "/ok", "/boom", "/denied", "/missing" })
        {
            seen[path] = await HeadersOn(app, path);
        }

        var report = string.Join(Environment.NewLine, seen.Select(s => $"{s.Key} -> {s.Value}"));
        Assert.True(seen.Values.All(h => h.Contains("X-Content-Type-Options: nosniff", StringComparison.Ordinal)
            && h.Contains("Content-Security-Policy: default-src 'none'; frame-ancestors 'none'", StringComparison.Ordinal)), report);
        Assert.StartsWith("500", seen["/boom"], StringComparison.Ordinal);
        Assert.Contains("Cache-Control: no-store", seen["/ok"], StringComparison.Ordinal);
        Assert.Contains("max-age=60", await HeadersOn(app, "/cached"), StringComparison.Ordinal);   // a deliberate cache header is kept
    }

    [Fact]
    public async Task SecurityHeaders_AddedBeforeNext_AreWipedFromThe500()
    {
        // The usual app.Use shape. Even as the first middleware, the exception handler's Response.Clear()
        // removes what it added before writing the ProblemDetails.
        await using var app = await StartAsync(_ => { }, a =>
        {
            a.Use((http, next) =>
            {
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return next(http);
            });
            a.UseExceptionHandler();
            MapStatuses(a);
        });

        var ok = await HeadersOn(app, "/ok");
        var boom = await HeadersOn(app, "/boom");

        Assert.True(ok.Contains("nosniff", StringComparison.Ordinal) && boom.StartsWith("500", StringComparison.Ordinal)
            && !boom.Contains("nosniff", StringComparison.Ordinal), $"/ok -> {ok}{Environment.NewLine}/boom -> {boom}");
    }

    [Fact]
    public async Task Hsts_IsSentOverHttpsOnly_NeverToLocalhost_AndDefaultsTo30Days()
    {
        Assert.Equal(TimeSpan.FromDays(30), new HstsOptions().MaxAge);
        await using var app = await StartAsync(
            s => s.AddHsts(o => { o.MaxAge = TimeSpan.FromDays(365); o.IncludeSubDomains = true; }),
            a => { a.UseHsts(); a.MapGet("/", () => "ok"); });

        var https = await HeadersOn(app, "https://orders.example.test/");
        var http = await HeadersOn(app, "http://orders.example.test/");
        var localhost = await HeadersOn(app, "https://localhost/");

        Assert.True(https.Contains("Strict-Transport-Security: max-age=31536000; includeSubDomains", StringComparison.Ordinal)
            && !http.Contains("Strict-Transport-Security", StringComparison.Ordinal)
            && !localhost.Contains("Strict-Transport-Security", StringComparison.Ordinal),
            $"https -> {https}{Environment.NewLine}http -> {http}{Environment.NewLine}localhost -> {localhost}");
    }

    // ---- A01 Broken Access Control: CORS ----------------------------------------------------------------

    private static async Task<(string? Origin, string? Credentials)> CorsAnswer(WebApplication app, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/visits");
        request.Headers.Add("Origin", origin);
        using var response = await app.GetTestClient().SendAsync(request);
        return (
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var o) ? o.Single() : null,
            response.Headers.TryGetValues("Access-Control-Allow-Credentials", out var c) ? c.Single() : null);
    }

    [Fact]
    public async Task Cors_AnswersOnlyThePortalsOrigins_WithoutCredentials()
    {
        await using var app = await StartAsync(s => s.AddPortalCors(["https://portal.example.test"]), a =>
        {
            a.UseCors(PortalCors.Policy);
            a.MapGet("/visits", () => "[]");
        });

        Assert.Equal<(string?, string?)>(("https://portal.example.test", null), await CorsAnswer(app, "https://portal.example.test"));
        Assert.Equal<(string?, string?)>((null, null), await CorsAnswer(app, "https://evil.example.test"));
    }

    [Fact]
    public async Task Cors_AnyOriginWithCredentials_IsRefusedAtStartup_ButReflectingTheOriginLetsAnySiteIn()
    {
        Assert.Throws<InvalidOperationException>(() => new CorsPolicyBuilder().AllowAnyOrigin().AllowCredentials().Build());

        await using var app = await StartAsync(s => s.AddCors(o => o.AddPolicy("open", p => p
            .SetIsOriginAllowed(_ => true).AllowCredentials().AllowAnyMethod().AllowAnyHeader())), a =>
        {
            a.UseCors("open");
            a.MapGet("/visits", () => "[]");
        });

        Assert.Equal<(string?, string?)>(("https://evil.example.test", "true"), await CorsAnswer(app, "https://evil.example.test"));
    }

    // ---- A08 Software or Data Integrity Failures: deserialization ---------------------------------------

    [Theory]
    [InlineData("""{"kind":"card","last4":"4242"}""", HttpStatusCode.OK, "CardPayment")]
    [InlineData("""{"kind":"System.Diagnostics.Process, System.Diagnostics.Process","last4":"4242"}""", HttpStatusCode.BadRequest, null)]
    public async Task PolymorphicBody_OnlyTheListedKindsAreCreated(string json, HttpStatusCode expected, string? type)
    {
        await using var app = await StartAsync(_ => { }, a =>
        {
            a.UseExceptionHandler();
            a.MapPost("/pay", ([FromBody] PaymentMethod method) => method.GetType().Name);
        });

        using var response = await app.GetTestClient().PostAsync("/pay", new StringContent(json, Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == expected && (type is null || body == type), $"{(int)response.StatusCode}: {body}");
    }

    [Fact]
    public void AllowOutOfOrderMetadataProperties_IsAlreadyOffByDefault()
    {
        // The old skill set it to false as ".NET 10 strict mode". It is the default, and turning it on only lets
        // the discriminator appear later in the object: a convenience, not a security switch.
        Assert.False(new JsonSerializerOptions().AllowOutOfOrderMetadataProperties);
    }

    // ---- A01 Broken Access Control: SSRF ----------------------------------------------------------------

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("10.20.30.40", false)]
    [InlineData("172.16.5.4", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    public void OnlyPublicAddressesPass(string address, bool expected) =>
        Assert.Equal(expected, OutboundGuard.IsPublic(IPAddress.Parse(address)));

    [Fact]
    public void IPAddress_HasNoIsPrivate()
    {
        // The old skill's SSRF check called ip.IsPrivate(). There is no such method: use IPNetwork.Contains.
        Assert.Null(typeof(IPAddress).GetMethod("IsPrivate"));
    }

    [Fact]
    public async Task OutboundGuard_RefusesAnInternalService_ThatAPlainClientReaches()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Logging.ClearProviders();
        await using var internalService = builder.Build();
        internalService.MapGet("/", () => "internal");
        await internalService.StartAsync();
        var port = new Uri(internalService.Urls.First()).Port;

        using (var plain = new HttpClient(new SocketsHttpHandler { UseProxy = false }))
        {
            Assert.Equal("internal", await plain.GetStringAsync(new Uri($"http://127.0.0.1:{port}/")));
        }

        var services = new ServiceCollection();
        services.AddPublicOnlyHttpClient("partner-webhooks");
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("partner-webhooks");

        foreach (var host in new[] { "127.0.0.1", "localhost" })
        {
            var error = await Record.ExceptionAsync(() => client.GetStringAsync(new Uri($"http://{host}:{port}/")));
            Assert.True(IsBlocked(error), $"{host}: {error}");
        }
    }

    private static bool IsBlocked(Exception? error)
    {
        for (; error is not null; error = error.InnerException)
        {
            if (error is BlockedDestinationException)
            {
                return true;
            }
        }

        return false;
    }
}
