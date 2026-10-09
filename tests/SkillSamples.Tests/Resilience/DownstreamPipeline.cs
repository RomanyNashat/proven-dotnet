using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace SkillSamples.Resilience;

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
