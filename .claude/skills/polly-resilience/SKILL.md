---
name: polly-resilience
description: Resilience for .NET with Polly v8: the standard HTTP handler (no retries on POST), custom pipelines, strategy order, timeouts, circuit breaker, fallback. Code tested in CI.
version: 1.1.0
---

# Polly v8 Resilience Patterns

The code in this skill is compiled and tested in CI (`tests/SkillSamples.Tests/Resilience`): a GET is
retried, a POST isn't, a slow attempt is cut and retried, the total timeout bounds every retry, and an
open circuit fails fast without calling downstream.

## HTTP clients: the standard resilience handler

<!-- sample: tests/SkillSamples.Tests/Resilience/PaymentClient.cs -->
```csharp
public sealed record PaymentStatus(string Reference, string State);

public sealed class PaymentClient(HttpClient http)
{
    public Task<HttpResponseMessage> GetStatusAsync(string reference, CancellationToken ct) =>
        http.GetAsync($"payments/{Uri.EscapeDataString(reference)}", ct);

    public Task<HttpResponseMessage> ChargeAsync(string reference, decimal amount, CancellationToken ct) =>
        http.PostAsJsonAsync("payments", new { reference, amount }, ct);
}

public static class PaymentClientRegistration
{
    public static IHttpClientBuilder AddPaymentClient(this IServiceCollection services, Uri baseAddress)
    {
        var builder = services.AddHttpClient<PaymentClient>(client =>
        {
            client.BaseAddress = baseAddress;
            // The resilience handler owns the timeouts. An HttpClient.Timeout at or below the total
            // timeout races it and surfaces as TaskCanceledException.
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);
            // A POST that timed out may still have charged the card. Retrying it can charge twice, so
            // only safe methods (GET, HEAD, OPTIONS...) are retried. Make a POST retryable only with an
            // idempotency key the other side honours.
            options.Retry.DisableForUnsafeHttpMethods();
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);   // at least 2 x attempt timeout
        });
        return builder;
    }
}
```

**Pipeline order** (outer → inner): rate limiter → total timeout → retry → circuit breaker → attempt
timeout → the HTTP call.

**Never retry a POST by default.** The standard handler retries every method unless told otherwise. A
payment or booking POST that timed out may have succeeded on the other side, so a retry can charge or
book twice. `DisableForUnsafeHttpMethods()` stops that. It needs **`Microsoft.Extensions.Http.Resilience`
9.8.0 or later**: earlier versions ignored it and still retried POST (dotnet/extensions #6548). Retry a
POST only when the other side honours an idempotency key you send.

## Custom pipelines (non-HTTP calls)

<!-- sample: tests/SkillSamples.Tests/Resilience/DownstreamPipeline.cs -->
```csharp
public sealed class TransientDownstreamException(string message) : Exception(message);

public sealed record DownstreamResilienceOptions
{
    public TimeSpan TotalTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int MaxRetries { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(15);
}

public static class DownstreamPipeline
{
    public const string Name = "downstream";

    // Outer to inner: total timeout -> retry -> circuit breaker -> attempt timeout -> the call.
    // The breaker sits inside the retry, so it counts every attempt, and an open circuit
    // (BrokenCircuitException) isn't retried: the caller gets it at once.
    public static IServiceCollection AddDownstreamPipeline(this IServiceCollection services, DownstreamResilienceOptions o) =>
        services.AddResiliencePipeline(Name, pipeline => pipeline
            .AddTimeout(o.TotalTimeout)
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = o.MaxRetries,
                Delay = o.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,   // spreads retries from many pods so they don't arrive together
                ShouldHandle = new PredicateBuilder()
                    .Handle<TransientDownstreamException>()
                    .Handle<TimeoutRejectedException>(),
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = o.BreakDuration,
                ShouldHandle = new PredicateBuilder()
                    .Handle<TransientDownstreamException>()
                    .Handle<TimeoutRejectedException>(),
            })
            .AddTimeout(o.AttemptTimeout));
}
```

Register with options from configuration (`services.AddDownstreamPipeline(options)`), then inject it:

```csharp
public sealed class ReportGateway([FromKeyedServices(DownstreamPipeline.Name)] ResiliencePipeline pipeline, IReportApi api)
{
    public ValueTask<Report> GetAsync(int id, CancellationToken ct) =>
        pipeline.ExecuteAsync(async token => await api.GetAsync(id, token), ct);
}
```

Always pass the token Polly gives you (`token`) to the call, not the outer `ct`. Polly's timeouts work by
cancelling that token; a call that ignores it can't be cut.

**Don't wrap a Kafka producer in a Polly retry.** The Confluent client already retries internally
(`MessageSendMaxRetries`), and a second retry layer around `ProduceAsync` sends duplicates unless the
producer is idempotent (`EnableIdempotence = true`). For reliable publishing use the outbox
(`outbox`), not retries.

## Strategy order, in one rule

The **total timeout is outermost** and the **attempt timeout innermost**; retry sits between them, and
the circuit breaker sits inside the retry. Reversing the timeouts makes the "total" timeout apply to each
attempt, so retries never stop. Putting the breaker outside the retry makes it see one failure per
call instead of one per attempt, so it opens late.

## Logging retries and circuit changes

```csharp
OnRetry = args =>
{
    logger.LogWarning("Retry {Attempt} after {DelayMs} ms: {Error}",
        args.AttemptNumber, args.RetryDelay.TotalMilliseconds, args.Outcome.Exception?.GetType().Name);
    return ValueTask.CompletedTask;
},
```

Log the exception type, not its message: a downstream error message can echo the request, including
patient data. Circuit breaker: `OnOpened` and `OnClosed` on `CircuitBreakerStrategyOptions`, the same way.

## Fallback

```csharp
.AddFallback(new FallbackStrategyOptions<ClinicInfo?>
{
    ShouldHandle = new PredicateBuilder<ClinicInfo?>().Handle<BrokenCircuitException>().Handle<TimeoutRejectedException>(),
    FallbackAction = _ => Outcome.FromResultAsValueTask(cachedClinic),
})
```

A fallback is for data that can be stale (a clinic's name, a lookup list). Never for anything the caller
acts on as current: a balance, an appointment slot, a permission.

## Polly v7 → v8

```csharp
// v7
var policy = Policy.Handle<HttpRequestException>()
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
await policy.ExecuteAsync(() => httpClient.GetAsync(url));

// v8
var pipeline = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        Delay = TimeSpan.FromSeconds(1),
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(),
    })
    .Build();
await pipeline.ExecuteAsync(async token => await httpClient.GetAsync(url, token), ct);
```

- `Policy` → `ResiliencePipelineBuilder` / `ResiliencePipeline`; `PolicyWrap` → chained `.Add*()`.
- `Handle<T>()` → `ShouldHandle = new PredicateBuilder().Handle<T>()`.
- Jitter is `UseJitter = true` with `Exponential`. There is no `ExponentialWithJitter` value in v8.
- DI: `AddResiliencePipeline("name", ...)`, resolved by `[FromKeyedServices("name")]` or
  `ResiliencePipelineProvider<string>`.
