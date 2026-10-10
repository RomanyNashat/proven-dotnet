using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Registry;
using Polly.Timeout;
using SkillSamples.Production;
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

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ThePaymentProviderHasABadMinute_StatusChecksRecover_AndTheChargeIsNeverSentTwice()
    {
        // Given: the provider answers 503 once to a status check, then 503 to a charge
        var (client, handler, sp) = PaymentClientWith(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
        using var _ = sp;

        // When: the app checks a payment, then charges a card
        var status = await client.GetStatusAsync("PAY-7", default);
        var charge = await client.ChargeAsync("PAY-8", 150m, default);

        // Then: the check succeeds on its retry; the charge is tried once and the failure goes back to the
        // caller, who asks the provider what happened instead of charging again
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, charge.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_AnArabicReferenceIsEscaped_AndTheRetryStillWorks()
    {
        ProductionConditions.Require();
        var seen = new List<string>();
        var calls = 0;
        var services = new ServiceCollection();
        services.AddPaymentClient(new Uri("https://payments.test/"))
            .ConfigurePrimaryHttpMessageHandler(() => new LambdaHandler(request =>
            {
                seen.Add(request.RequestUri!.AbsolutePath);
                return new HttpResponseMessage(++calls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            }));
        using var sp = services.BuildServiceProvider();

        var response = await sp.GetRequiredService<PaymentClient>().GetStatusAsync("دفع ٧", default);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, seen.Count);
        Assert.All(seen, path => Assert.Equal("/payments/%D8%AF%D9%81%D8%B9%20%D9%A7", path));
    }

    private sealed class LambdaHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
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
