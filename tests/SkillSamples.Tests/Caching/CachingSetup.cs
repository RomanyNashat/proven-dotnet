using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Caching;

public static class CachingSetup
{
    public static IServiceCollection AddTwoLevelCache(this IServiceCollection services, string redisConnectionString)
    {
        services.AddStackExchangeRedisCache(o => o.Configuration = redisConnectionString);   // L2: shared by every pod
        services.AddHybridCache(o => o.DefaultEntryOptions = new HybridCacheEntryOptions
        {
            Expiration = TimeSpan.FromMinutes(10),            // L2
            LocalCacheExpiration = TimeSpan.FromMinutes(2),   // L1, per pod: shorter than L2
        });
        return services;
    }
}
