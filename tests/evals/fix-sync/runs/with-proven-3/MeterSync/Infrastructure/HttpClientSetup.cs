using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace MeterSync.Infrastructure;

public static class HttpClientSetup
{
    public static IServiceCollection AddMeterVendorClient(this IServiceCollection services)
    {
        services.AddOptions<MeterVendorOptions>()
            .BindConfiguration(MeterVendorOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // GETs only: the standard handler's retries are safe here.
        services.AddHttpClient<MeterVendorClient>((sp, http) =>
                http.BaseAddress = WithTrailingSlash(sp.GetRequiredService<IOptions<MeterVendorOptions>>().Value.BaseAddress))
            .AddStandardResilienceHandler();
        return services;
    }

    public static IServiceCollection AddSmsClient(this IServiceCollection services)
    {
        services.AddOptions<SmsOptions>()
            .BindConfiguration(SmsOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<SmsClient>((sp, http) =>
            {
                http.BaseAddress = WithTrailingSlash(sp.GetRequiredService<IOptions<SmsOptions>>().Value.BaseAddress);

                // The resilience handler owns the timeout. HttpClient's own surfaces as a TaskCanceledException,
                // which looks like shutdown to the code above it.
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions o, IServiceProvider sp) =>
            {
                // Every POST sends a message and the gateway has no idempotency key: a retry is a second SMS.
                o.Retry.DisableForUnsafeHttpMethods();
                var timeout = sp.GetRequiredService<IOptions<SmsOptions>>().Value.Timeout;
                o.AttemptTimeout.Timeout = timeout;
                o.TotalRequestTimeout.Timeout = timeout * 2;
            });
        return services;
    }

    /// <summary>
    /// "https://meters.example.net/v2" plus "meters/..." resolves to "/meters/...": without the trailing slash
    /// the last path segment is dropped. Production's setting has none, so it's added here.
    /// </summary>
    public static Uri WithTrailingSlash(Uri baseAddress) =>
        baseAddress.AbsolutePath.EndsWith('/') ? baseAddress : new UriBuilder(baseAddress) { Path = baseAddress.AbsolutePath + "/" }.Uri;
}
