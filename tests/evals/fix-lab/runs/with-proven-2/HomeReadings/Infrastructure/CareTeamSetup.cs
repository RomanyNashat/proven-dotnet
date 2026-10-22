using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

public static class CareTeamSetup
{
    /// <summary>
    /// The typed care-team client. A POST is never retried: the care team has no idempotency key, so a
    /// retried POST that had in fact arrived is a second alert to a nurse.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The client builder.</returns>
    public static IHttpClientBuilder AddCareTeamClient(this IServiceCollection services)
    {
        services.AddOptions<CareTeamOptions>()
            .BindConfiguration(CareTeamOptions.Section)
            .ValidateDataAnnotations()
            .Validate(o => o.BaseAddress is null || o.BaseAddress.IsAbsoluteUri, "CareTeam:BaseAddress must be an absolute URL.")
            .ValidateOnStart();

        var builder = services.AddHttpClient<CareTeamClient>((provider, client) =>
        {
            client.BaseAddress = WithTrailingSlash(provider.GetRequiredService<IOptions<CareTeamOptions>>().Value.BaseAddress!);
            client.Timeout = Timeout.InfiniteTimeSpan;   // the resilience handler owns the timeouts
        });

        builder.AddStandardResilienceHandler(o =>
        {
            o.Retry.DisableForUnsafeHttpMethods();
            // One attempt for a POST, so give it nearly the whole budget: an attempt cut short may still
            // have reached the care team, and the alert is then sent again on the next run.
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(25);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);   // at least twice the attempt timeout
        });
        return builder;
    }

    // Without the trailing '/', "alerts" replaces the last path segment instead of being added to it.
    private static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");
}
