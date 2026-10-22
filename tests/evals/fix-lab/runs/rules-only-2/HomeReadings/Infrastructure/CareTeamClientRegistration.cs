using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

public static class CareTeamClientRegistration
{
    /// <summary>
    /// The care team's typed client: timeouts and a circuit breaker, but no retry of the POST, because every
    /// POST is a new alert to a nurse (no idempotency key). Retrying is the notifier's decision, made only when
    /// the alert certainly did not arrive.
    /// </summary>
    public static IHttpClientBuilder AddCareTeamClient(this IServiceCollection services)
    {
        var builder = services.AddHttpClient<CareTeamClient>((sp, c) =>
            c.BaseAddress = sp.GetRequiredService<IOptions<CareTeamOptions>>().Value.BaseAddress);
        builder.AddStandardResilienceHandler(o => o.Retry.DisableForUnsafeHttpMethods());
        return builder;
    }
}
