using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace HomeReadings.Infrastructure;

public static class CareTeamRegistration
{
    public static IServiceCollection AddCareTeamClient(this IServiceCollection services)
    {
        services.AddOptions<CareTeamOptions>()
            .BindConfiguration(CareTeamOptions.Section)
            .Validate(o => Uri.TryCreate(o.BaseAddress, UriKind.Absolute, out _), "CareTeam:BaseAddress must be an absolute URL.")
            .ValidateOnStart();

        services.AddHttpClient<CareTeamClient>((sp, client) =>
            {
                client.BaseAddress = new Uri(sp.GetRequiredService<IOptions<CareTeamOptions>>().Value.BaseAddress);
                client.Timeout = Timeout.InfiniteTimeSpan;   // the resilience handler owns the timeouts
            })
            .AddStandardResilienceHandler(o =>
            {
                // The care team has no idempotency key: a POST that timed out may already have paged a
                // nurse, so it is never retried here. The notifier decides what to do with the outcome.
                o.Retry.DisableForUnsafeHttpMethods();
            });
        return services;
    }
}
