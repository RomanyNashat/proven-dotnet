using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SkillSamples.Kubernetes;

public static class Probes
{
    public static IHealthChecksBuilder AddProbes(this IServiceCollection services, string redisConnectionString) =>
        services.AddHealthChecks()
            // Liveness: is the process alive? Nothing outside it, or an outage restarts every pod.
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            // Readiness: can it serve traffic? One check per dependency (.AddNpgSql, .AddSqlServer, .AddKafka).
            .AddRedis(redisConnectionString, name: "redis", tags: ["ready"]);

    public static WebApplication MapProbes(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        return app;
    }
}
