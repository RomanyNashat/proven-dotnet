using LabResults.Application;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace LabResults.Infrastructure;

public static class HttpClientsRegistration
{
    public const string Labs = "Labs";
    public const string Sms = "Sms";

    /// <summary>The lab and SMS typed clients, with validated base addresses, timeouts and no URI logging.</summary>
    /// <param name="services">The services.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddDownstreamClients(this IServiceCollection services)
    {
        AddBaseAddress(services, Labs);
        AddBaseAddress(services, Sms);

        services.AddHttpClient<LabClient>((sp, http) => Configure(sp, http, Labs))
            .RemoveAllLoggers()
            .AddHttpMessageHandler(sp => new StatusOnlyLoggingHandler(nameof(LabClient), sp.GetRequiredService<ILogger<StatusOnlyLoggingHandler>>()))
            .AddStandardResilienceHandler(o =>
            {
                // A GET: safe to retry.
                o.Retry.MaxRetryAttempts = 3;
                o.Retry.Delay = TimeSpan.FromMilliseconds(300);
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });

        services.AddHttpClient<SmsClient>((sp, http) => Configure(sp, http, Sms))
            .RemoveAllLoggers()
            .AddHttpMessageHandler(sp => new StatusOnlyLoggingHandler(nameof(SmsClient), sp.GetRequiredService<ILogger<StatusOnlyLoggingHandler>>()))
            .AddStandardResilienceHandler(o =>
            {
                // A POST that timed out may have been sent: a retry texts the patient twice and is billed twice.
                o.Retry.DisableForUnsafeHttpMethods();
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(15);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            });
        services.AddScoped<ISmsSender>(sp => sp.GetRequiredService<SmsClient>());

        return services;
    }

    private static void AddBaseAddress(IServiceCollection services, string section) =>
        services.AddOptions<BaseAddressOptions>(section)
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .Validate(o => o.BaseAddress is null || (o.BaseAddress.IsAbsoluteUri && o.BaseAddress.Scheme is "https" or "http"),
                $"{section}:BaseAddress must be an absolute http(s) URL.")
            .ValidateOnStart();

    private static void Configure(IServiceProvider sp, HttpClient http, string section)
    {
        http.BaseAddress = sp.GetRequiredService<IOptionsMonitor<BaseAddressOptions>>().Get(section).WithTrailingSlash();
        http.Timeout = Timeout.InfiniteTimeSpan;   // the resilience handler owns the timeouts
    }
}
