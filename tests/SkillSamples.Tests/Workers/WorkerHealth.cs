using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace SkillSamples.Workers;

public sealed class WorkerHealthCheck<TWorker>(TWorker worker) : IHealthCheck where TWorker : BackgroundService
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct) =>
        Task.FromResult(worker.ExecuteTask switch
        {
            null => HealthCheckResult.Degraded("Worker has not started"),
            { IsFaulted: true } t => HealthCheckResult.Unhealthy("Worker has faulted", t.Exception),
            { IsCompleted: true } => HealthCheckResult.Unhealthy("Worker has stopped"),
            _ => HealthCheckResult.Healthy("Worker is running"),
        });
}

public static class WorkerRegistration
{
    // AddHostedService<T>() alone registers the worker only as IHostedService, so nothing can inject it
    // as T, and a health check that asks for T fails to resolve. Register the instance once as itself,
    // and hand that same instance to the host.
    public static IServiceCollection AddWorkerWithHealthCheck<TWorker>(this IServiceCollection services, string name)
        where TWorker : BackgroundService
    {
        services.AddSingleton<TWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<TWorker>());
        services.AddHealthChecks().AddCheck<WorkerHealthCheck<TWorker>>(name, tags: ["ready"]);
        return services;
    }
}
