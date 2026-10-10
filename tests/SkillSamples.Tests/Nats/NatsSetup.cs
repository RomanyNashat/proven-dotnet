using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Net;

namespace SkillSamples.Nats;

public sealed record OrderCreated(int OrderId);

public static class NatsSetup
{
    // One long-lived client per service. NatsClient (package NATS.Net) sends records as JSON; a bare
    // NatsConnection with default options handles only strings, numbers and bytes, and throws on a record.
    public static IServiceCollection AddNats(this IServiceCollection services, string url, string serviceName) =>
        services.AddSingleton<INatsClient>(_ => new NatsClient(new NatsOpts { Url = url, Name = serviceName }));
}
