using Microsoft.Extensions.Options;

namespace ClinicReminders.Infrastructure;

public static class SmsSetup
{
    public static IHttpClientBuilder AddSmsClient(this IServiceCollection services)
    {
        services.AddOptions<SmsOptions>()
            .BindConfiguration(SmsOptions.Section)
            .ValidateDataAnnotations()
            .Validate(o => o.BaseAddress is null || o.BaseAddress.AbsolutePath.EndsWith('/'),
                "Sms:BaseAddress must end with '/', or its last path segment is dropped from every call.")
            .ValidateOnStart();   // a bad setting stops the deploy, not the first SMS

        // No resilience handler: the provider has no idempotency key, so the POST must not be retried.
        return services.AddHttpClient<SmsClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<SmsOptions>>().Value;
            client.BaseAddress = options.BaseAddress;
            client.Timeout = options.Timeout;
        });
    }
}
