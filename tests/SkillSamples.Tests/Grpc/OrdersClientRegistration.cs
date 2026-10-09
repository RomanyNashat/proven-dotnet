using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkillSamples.GrpcSamples.V1;

namespace SkillSamples.GrpcSamples;

public static class OrdersClientRegistration
{
    // gRPC's own retries, from the channel's service config. Only reads: every gRPC call is an HTTP POST,
    // and a retried create could run twice.
    private static readonly MethodConfig ReadRetries = new()
    {
        Names = { new MethodName { Service = "orders.v1.Orders", Method = "GetOrder" } },
        RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialBackoff = TimeSpan.FromMilliseconds(100),
            MaxBackoff = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2,
            RetryableStatusCodes = { StatusCode.Unavailable },
        },
    };

    public static IHttpClientBuilder AddOrdersClient(this IServiceCollection services, Uri address) =>
        services.AddGrpcClient<Orders.OrdersClient>(o => o.Address = address)
            .ConfigureChannel(channel => channel.ServiceConfig = new ServiceConfig { MethodConfigs = { ReadRetries } })
            .AddInterceptor(sp => new DefaultDeadlineInterceptor(sp.GetRequiredService<TimeProvider>(), TimeSpan.FromSeconds(5)));
}
