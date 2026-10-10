using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SkillSamples.Core;

public static class ShippingSetup
{
    public static IHttpClientBuilder AddShipping(this IServiceCollection services)
    {
        services.AddOptions<ShippingOptions>()
            .BindConfiguration(ShippingOptions.Section)
            .ValidateDataAnnotations()
            .Validate(o => o.BaseAddress is null || o.BaseAddress.AbsolutePath.EndsWith('/'),
                "Shipping:BaseAddress must end with '/', or its last path segment is dropped from every call.")
            .ValidateOnStart();   // a bad setting stops the deploy, not the first shipment

        // A typed client from IHttpClientFactory; retries and timeouts come from `polly-resilience`.
        return services.AddHttpClient<ShippingClient>((provider, client) =>
            client.BaseAddress = provider.GetRequiredService<IOptions<ShippingOptions>>().Value.BaseAddress);
    }
}
