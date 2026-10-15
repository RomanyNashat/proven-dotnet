using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SkillSamples.Production;
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

    private static async Task<(HttpStatusCode Status, string Body)> UploadAsync(int megabytes, string clientIp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{BehindNginxApp.ProxyUrl}/upload")
        {
            Content = new ByteArrayContent(new byte[megabytes * 1024 * 1024]),
        };
        request.Headers.Add("X-Forwarded-For", clientIp);
        request.Headers.Add("X-Forwarded-Proto", "https");
        using var response = await Http.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_APatientUploadsA6MbScan_NginxRefusesIt_SoOnlyNginxsLogKnows_TheRetakenScanGoesThrough()
    {
        // Given: a patient's phone uploads a 6 MB scan through the ingress
        var tooBig = await UploadAsync(megabytes: 6, clientIp: "203.0.113.20");

        // When: support looks for it, then the patient retakes it smaller
        var retaken = await UploadAsync(megabytes: 1, clientIp: "203.0.113.20");
        using var whoami = new HttpRequestMessage(HttpMethod.Get, $"{BehindNginxApp.ProxyUrl}/whoami");
        whoami.Headers.Add("X-Forwarded-For", "203.0.113.20");
        whoami.Headers.Add("X-Forwarded-Proto", "https");
        var seen = await (await Http.SendAsync(whoami)).Content.ReadAsStringAsync();

        // Then: the 413 is nginx's own page (the app never saw the request, so its logs are empty and the
        // answer is in nginx's), the smaller one reaches the app whole, and the app sees the patient
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.Status);
        Assert.Contains("nginx", tooBig.Body, StringComparison.Ordinal);
        Assert.Equal((HttpStatusCode.OK, (1024 * 1024).ToString(System.Globalization.CultureInfo.InvariantCulture)), retaken);
        Assert.Equal("203.0.113.20 https", seen);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_TheAppBehindNginxSeesTheClient_AndTakesTheUpload()
    {
        ProductionConditions.Require();

        var upload = await UploadAsync(megabytes: 1, clientIp: "203.0.113.21");
        using var whoami = new HttpRequestMessage(HttpMethod.Get, $"{BehindNginxApp.ProxyUrl}/whoami");
        whoami.Headers.Add("X-Forwarded-For", "203.0.113.21");
        whoami.Headers.Add("X-Forwarded-Proto", "https");

        Assert.Equal(HttpStatusCode.OK, upload.Status);
        Assert.Equal("203.0.113.21 https", await (await Http.SendAsync(whoami)).Content.ReadAsStringAsync());
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
