using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace SkillSamples.Resilience;

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
