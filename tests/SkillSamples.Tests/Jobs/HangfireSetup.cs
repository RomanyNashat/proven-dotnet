using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.DependencyInjection;

namespace SkillSamples.Jobs;

public static class HangfireSetup
{
    public static IServiceCollection AddJobs(this IServiceCollection services, string connectionString)
    {
        services.AddHangfire(config => config
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

        services.AddHangfireServer(options =>
        {
            options.Queues = ["critical", "default", "low"];   // fetched in this order
            options.SchedulePollingInterval = TimeSpan.FromSeconds(5);
        });
        return services;
    }
}
