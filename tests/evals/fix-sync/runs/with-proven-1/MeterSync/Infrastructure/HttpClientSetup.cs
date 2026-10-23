using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace MeterSync.Infrastructure;

public static class HttpClientSetup
{
    public const string MeterVendorSection = "MeterVendor";
    public const string SmsSection = "Sms";

    // The vendor returns every reading a meter ever uploaded; this caps one answer well above a year of them.
    public const long MaxVendorResponseBytes = 4 * 1024 * 1024;

    public static readonly TimeSpan SmsTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// "https://meters.example.net/v2" + "meters/..." resolves to "/meters/...": without a trailing slash the
    /// last segment is dropped. Production's setting has none, so it's added rather than refused.
    /// </summary>
    public static Uri WithTrailingSlash(Uri address) =>
        address.AbsolutePath.EndsWith('/') ? address : new UriBuilder(address) { Path = address.AbsolutePath + "/" }.Uri;

    public static IServiceCollection AddDownstreamClients(this IServiceCollection services)
    {
        AddAddress(services, MeterVendorSection);
        AddAddress(services, SmsSection);

        services.AddHttpClient<MeterVendorClient>((sp, http) =>
            {
                http.BaseAddress = Address(sp, MeterVendorSection);
                http.MaxResponseContentBufferSize = MaxVendorResponseBytes;
            })
            .RemoveAllLoggers()   // the default logs carry the URL, which carries the meter serial
            .AddStandardResilienceHandler();   // GETs only: retries are safe

        services.AddHttpClient<SmsClient>((sp, http) =>
            {
                http.BaseAddress = Address(sp, SmsSection);
                http.Timeout = Timeout.InfiniteTimeSpan;   // the resilience timeout below owns it
            })
            .RemoveAllLoggers()
            // A timeout and nothing else. Never a retry: every POST sends an SMS, and the gateway has no
            // idempotency key, so a retried POST texts the patient twice.
            .AddResilienceHandler("sms", pipeline => pipeline.AddTimeout(SmsTimeout));

        services.AddTransient<Application.IMeterVendor>(sp => sp.GetRequiredService<MeterVendorClient>());
        services.AddTransient<Application.ISmsSender>(sp => sp.GetRequiredService<SmsClient>());
        return services;
    }

    private static void AddAddress(IServiceCollection services, string section) =>
        services.AddOptions<ServiceAddressOptions>(section)
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .Validate(o => o.BaseAddress is null || o.BaseAddress.IsAbsoluteUri, $"{section}:BaseAddress must be an absolute URL.")
            .ValidateOnStart();

    private static Uri Address(IServiceProvider sp, string section) =>
        WithTrailingSlash(sp.GetRequiredService<IOptionsMonitor<ServiceAddressOptions>>().Get(section).BaseAddress!);
}
