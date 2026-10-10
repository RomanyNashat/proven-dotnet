using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.SignalR;

// Stands in for the real token: the user id comes from a header.
public sealed class HeaderUserAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
        Task.FromResult(Request.Headers.TryGetValue("X-User", out var user)
            ? AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "Header")), "Header"))
            : AuthenticateResult.NoResult());
}

public sealed class SignalRTests : IAsyncLifetime
{
    private static readonly string Redis = Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379";
    private readonly string _prefix = $"tests-{Guid.NewGuid():N}";
    private readonly List<IAsyncDisposable> _cleanup = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var item in Enumerable.Reverse(_cleanup))
            await item.DisposeAsync();
    }

    // One "pod": an app with the hub, with or without the backplane.
    private async Task<WebApplication> PodAsync(bool backplane)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("Header").AddScheme<AuthenticationSchemeOptions, HeaderUserAuth>("Header", null);
        builder.Services.AddAuthorization();
        if (backplane)
            builder.Services.AddRealtime(Redis, _prefix);
        else
            builder.Services.AddSingleton<PatientNotifier>().AddSignalR();

        var app = builder.Build();
        app.MapHub<NotificationsHub>("/hubs/notifications");
        await app.StartAsync();
        _cleanup.Add(app);
        return app;
    }

    private async Task<(HubConnection Connection, ConcurrentQueue<string> Received)> ConnectAsync(
        string user, Func<HttpMessageHandler> handler)
    {
        var received = new ConcurrentQueue<string>();
        var connection = new HubConnectionBuilder()
            .WithUrl("http://pod/hubs/notifications", o =>
            {
                o.Transports = HttpTransportType.LongPolling;   // TestServer has no real sockets
                o.HttpMessageHandlerFactory = _ => handler();
                o.Headers["X-User"] = user;
            })
            .Build();
        connection.On<string>("Notify", received.Enqueue);
        _cleanup.Add(connection);
        await connection.StartAsync();
        return (connection, received);
    }

    private static async Task<bool> Arrives(ConcurrentQueue<string> received, string message, TimeSpan within)
    {
        var until = DateTime.UtcNow + within;
        while (DateTime.UtcNow < until)
        {
            if (received.Contains(message))
                return true;
            await Task.Delay(100);
        }
        return false;
    }

    private static Task NotifyFrom(WebApplication pod, string patient, string message) =>
        pod.Services.GetRequiredService<PatientNotifier>().NotifyAsync(patient, message, CancellationToken.None);

    // Sends again until it arrives: right after a client connects, its pod may still be subscribing to the
    // backplane, and a message sent in that moment is missed (seen once in CI). Clients refetch on connect.
    private static async Task<bool> ArrivesAcrossPods(WebApplication from, string patient, string message, ConcurrentQueue<string> received)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await NotifyFrom(from, patient, message);
            if (await Arrives(received, message, TimeSpan.FromSeconds(1)))
                return true;
        }
        return false;
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_APatientIsConnectedToPodA_PodBSendsTheUpdate_ItArrives()
    {
        var (a, b) = (await PodAsync(backplane: true), await PodAsync(backplane: true));
        var (_, patient42) = await ConnectAsync("42", () => a.GetTestServer().CreateHandler());
        var (_, patient43) = await ConnectAsync("43", () => a.GetTestServer().CreateHandler());

        Assert.True(await ArrivesAcrossPods(b, "42", "Your results are ready", patient42));
        Assert.False(await Arrives(patient43, "Your results are ready", TimeSpan.FromSeconds(1)));   // only that patient
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_WithoutTheBackplane_TheUpdateSentFromAnotherPodNeverArrives()
    {
        var (a, b) = (await PodAsync(backplane: false), await PodAsync(backplane: false));
        var (_, patient42) = await ConnectAsync("42", () => a.GetTestServer().CreateHandler());

        await NotifyFrom(b, "42", "Your results are ready");

        Assert.False(await Arrives(patient42, "Your results are ready", TimeSpan.FromSeconds(3)));
        await NotifyFrom(a, "42", "Sent from the same pod");
        Assert.True(await Arrives(patient42, "Sent from the same pod", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheLoadBalancerSendsTheNextRequestToAnotherPod_TheConnectionFailsWithoutStickiness()
    {
        var (a, b) = (await PodAsync(backplane: true), await PodAsync(backplane: true));
        // Negotiate lands on pod A, every request after it on pod B: what a load balancer without affinity does.
        var connect = () => ConnectAsync("42", () => new RoundRobin(a.GetTestServer().CreateHandler(), b.GetTestServer().CreateHandler()));

        await Assert.ThrowsAnyAsync<Exception>(connect);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcu_TheBackplaneDeliversAcrossPods()
    {
        ProductionConditions.Require();
        var (a, b) = (await PodAsync(backplane: true), await PodAsync(backplane: true));
        var (_, patient) = await ConnectAsync("7", () => a.GetTestServer().CreateHandler());

        Assert.True(await ArrivesAcrossPods(b, "7", "موعدك غدًا الساعة 10", patient));
    }

    // The first request goes to the first server, every later one to the second.
    private sealed class RoundRobin(HttpMessageHandler first, HttpMessageHandler rest) : HttpMessageHandler
    {
        private int _calls;
        private readonly HttpMessageInvoker _first = new(first), _rest = new(rest);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            (Interlocked.Increment(ref _calls) == 1 ? _first : _rest).SendAsync(request, ct);
    }
}
