using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Timeout;
using Xunit;

namespace SkillSamples.Resilience;

public sealed class ScriptedHandler(params HttpStatusCode[] script) : HttpMessageHandler
{
    private int _calls;

    public int Calls => _calls;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var i = Interlocked.Increment(ref _calls) - 1;
        return Task.FromResult(new HttpResponseMessage(script[Math.Min(i, script.Length - 1)]));
    }
}

public sealed class ResilienceTests
{
    private static (PaymentClient Client, ScriptedHandler Handler, ServiceProvider Sp) PaymentClientWith(params HttpStatusCode[] script)
    {
        var handler = new ScriptedHandler(script);
        var services = new ServiceCollection();
        services.AddPaymentClient(new Uri("https://payments.test/")).ConfigurePrimaryHttpMessageHandler(() => handler);
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<PaymentClient>(), handler, sp);
    }

    [Fact]
    public async Task Get_TransientFailure_IsRetried()
    {
        var (client, handler, sp) = PaymentClientWith(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var _ = sp;

        var response = await client.GetStatusAsync("PAY-1", default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Post_TransientFailure_IsNotRetried_SoNoDoubleCharge()
    {
        var (client, handler, sp) = PaymentClientWith(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var _ = sp;

        var response = await client.ChargeAsync("PAY-1", 150m, default);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    private static ResiliencePipeline Pipeline(DownstreamResilienceOptions options) =>
        new ServiceCollection().AddDownstreamPipeline(options).BuildServiceProvider()
            .GetRequiredService<ResiliencePipelineProvider<string>>().GetPipeline(DownstreamPipeline.Name);

    private static readonly DownstreamResilienceOptions Fast = new()
    {
        TotalTimeout = TimeSpan.FromSeconds(5),
        AttemptTimeout = TimeSpan.FromMilliseconds(100),
        RetryDelay = TimeSpan.FromMilliseconds(5),
        BreakDuration = TimeSpan.FromSeconds(30),
    };

    [Fact]
    public async Task Retry_RecoversFromTransientErrors()
    {
        var pipeline = Pipeline(Fast);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return attempts < 3 ? throw new TransientDownstreamException("busy") : ValueTask.FromResult(42);
        });

        Assert.Equal(42, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task AttemptTimeout_CutsASlowAttempt_AndRetries()
    {
        var pipeline = Pipeline(Fast);
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(async ct =>
        {
            if (++attempts == 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);   // honours the token, so the timeout cuts it
            }

            return "ok";
        });

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task TotalTimeout_BoundsAllRetries()
    {
        var pipeline = Pipeline(Fast with { TotalTimeout = TimeSpan.FromMilliseconds(300), MaxRetries = 50 });
        var watch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
            await pipeline.ExecuteAsync(async ct => await Task.Delay(TimeSpan.FromSeconds(10), ct)));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task OpenCircuit_FailsFast_WithoutCallingDownstream()
    {
        var pipeline = Pipeline(Fast with { MaxRetries = 1 });
        var calls = 0;

        for (var i = 0; i < 10; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(_ => { calls++; throw new TransientDownstreamException("down"); });
            }
            catch (Exception ex) when (ex is TransientDownstreamException or BrokenCircuitException)
            {
            }
        }

        var before = calls;
        await Assert.ThrowsAnyAsync<BrokenCircuitException>(async () =>
            await pipeline.ExecuteAsync(_ => { calls++; return ValueTask.CompletedTask; }));
        Assert.Equal(before, calls);
    }
}
