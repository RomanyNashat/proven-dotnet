using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace SkillSamples.SignalR;

public static class RealtimeSetup
{
    // Each pod knows only its own connections. The Redis backplane passes every send to all pods, so a
    // message sent on one reaches a client connected to another. The prefix keeps apps that share a
    // Redis apart.
    public static ISignalRServerBuilder AddRealtime(this IServiceCollection services, string redis, string channelPrefix)
    {
        services.AddSingleton<PatientNotifier>();
        return services.AddSignalR().AddStackExchangeRedis(redis, o =>
            o.Configuration.ChannelPrefix = RedisChannel.Literal(channelPrefix));
    }
}
