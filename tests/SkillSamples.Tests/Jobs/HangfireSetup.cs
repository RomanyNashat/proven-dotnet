using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Jobs;

public static class HangfireSetup
{
    // Every service that enqueues needs the storage; only the ones that run jobs add the servers.
    public static IServiceCollection AddJobStorage(this IServiceCollection services, string connectionString) =>
        services
            .AddSingleton<ITimeZoneResolver, TimeZoneResolver>()   // recurring jobs' zones on images without tzdata
            .AddHangfire(config => config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    options => options.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        PrepareSchemaIfNecessary = false,          // the schema is applied by the pipeline, not at boot
                        QueuePollInterval = TimeSpan.FromSeconds(1) // the default is 15 s: jobs wait that long to start
                    }));

    public static IServiceCollection AddJobServers(this IServiceCollection services)
    {
        // A server's queue order is a preference, not a reservation: its workers still fill up with other
        // jobs. Urgent work gets a server of its own, so a backlog elsewhere can't hold it up.
        services.AddHangfireServer(options => options.Queues = ["critical"]);
        services.AddHangfireServer(options =>
        {
            options.Queues = ["default", "low"];
            options.SchedulePollingInterval = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
