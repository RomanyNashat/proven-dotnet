using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SkillSamples.Nginx;

/// <summary>
/// CI runs nginx with the configs in conf/ (host network): the static site on :8088, and a proxy on :8089
/// in front of the Kestrel app this fixture starts on :5099.
/// </summary>
public sealed class BehindNginxApp : IAsyncLifetime
{
    public static readonly string StaticUrl = Environment.GetEnvironmentVariable("NGINX_STATIC_URL") ?? "http://localhost:8088";
    public static readonly string ProxyUrl = Environment.GetEnvironmentVariable("NGINX_PROXY_URL") ?? "http://localhost:8089";

    private WebApplication? _app;

    public TaskCompletionSource UnguardedCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:5099");
        builder.ConfigureBehindNginx("10.42.0.0/16");
        _app = builder.Build();
        _app.UseBehindNginx();

        _app.MapGet("/whoami", (HttpContext ctx) => $"{ctx.Connection.RemoteIpAddress} {ctx.Request.Scheme}");
        _app.MapPost("/upload", async (HttpContext ctx) =>
        {
            var length = 0L;
            var buffer = new byte[81920];
            int read;
            while ((read = await ctx.Request.Body.ReadAsync(buffer, ctx.RequestAborted)) > 0)
            {
                length += read;
            }

            return Results.Ok(length);
        });
        _app.MapGet("/slow", async (HttpContext ctx) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ctx.RequestAborted);   // the app's own 1 s timeout cancels this
            return "done";
        });
        _app.MapGet("/slow-unguarded", async (HttpContext ctx) =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ctx.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                UnguardedCancelled.TrySetResult();   // nginx gave up and closed the connection
                throw;
            }

            return "done";
        }).DisableRequestTimeout();
        await _app.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}

public sealed class NginxTests(BehindNginxApp app) : IClassFixture<BehindNginxApp>
{
    private static readonly HttpClient Http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None });

    [Fact]
    public async Task Static_Json_IsUtf8_Cacheable_PublicAndRevalidatable()
    {
        using var response = await Http.GetAsync($"{BehindNginxApp.StaticUrl}/ar/home.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("احجز فحصك السنوي", await response.Content.ReadAsStringAsync());
        var cacheControl = response.Headers.CacheControl!.ToString();
        Assert.Contains("max-age=60", cacheControl);
        Assert.Contains("stale-while-revalidate=300", cacheControl);
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("nginx", response.Headers.Server.ToString());   // no version

        using var again = new HttpRequestMessage(HttpMethod.Get, $"{BehindNginxApp.StaticUrl}/ar/home.json");
        again.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var notModified = await Http.SendAsync(again);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal("*", notModified.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task Static_LargerJson_IsGzipped()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BehindNginxApp.StaticUrl}/home.json");
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        using var response = await Http.SendAsync(request);

        Assert.Equal("gzip", response.Content.Headers.ContentEncoding.Single());
    }

    [Theory]
    [InlineData("/.git/config")]
    [InlineData("/.env")]
    [InlineData("/")]
    [InlineData("/ar/")]
    [InlineData("/../appsettings.json")]
    [InlineData("/%2e%2e/appsettings.json")]
    public async Task Static_NothingOutsideTheContentIsServed(string path)
    {
        using var response = await Http.GetAsync(BehindNginxApp.StaticUrl + path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("do-not-serve", body);
        Assert.DoesNotContain("Index of", body);
    }

    [Fact]
    public async Task Static_IsReadOnly()
    {
        using var response = await Http.PostAsync($"{BehindNginxApp.StaticUrl}/home.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Proxy_AppSeesTheClientAndScheme()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BehindNginxApp.ProxyUrl}/whoami");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");   // as the ingress would send it
        request.Headers.Add("X-Forwarded-Proto", "https");

        var seen = await (await Http.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Equal("203.0.113.9 https", seen);
    }

    [Fact]
    public async Task Proxy_BodyOverTheLimit_IsStoppedByNginx_UnderItReachesTheApp()
    {
        using var small = await Http.PostAsync($"{BehindNginxApp.ProxyUrl}/upload", new ByteArrayContent(new byte[1024 * 1024]));
        Assert.Equal(HttpStatusCode.OK, small.StatusCode);

        using var large = await Http.PostAsync($"{BehindNginxApp.ProxyUrl}/upload", new ByteArrayContent(new byte[6 * 1024 * 1024]));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.Contains("nginx", await large.Content.ReadAsStringAsync());   // nginx's page, not the app's
    }

    [Fact]
    public async Task Proxy_AppTimesOutFirst_TheCallerGetsTheAppsAnswerQuickly()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var response = await Http.GetAsync($"{BehindNginxApp.ProxyUrl}/slow");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.DoesNotContain("nginx", await response.Content.ReadAsStringAsync());   // the app answered
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2.5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Proxy_NoAppTimeout_NginxAnswers504_AndTheAppIsToldToStop()
    {
        using var response = await Http.GetAsync($"{BehindNginxApp.ProxyUrl}/slow-unguarded");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Contains("nginx", await response.Content.ReadAsStringAsync());   // nginx's page
        await app.UnguardedCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));    // RequestAborted fired in the app
    }
}
