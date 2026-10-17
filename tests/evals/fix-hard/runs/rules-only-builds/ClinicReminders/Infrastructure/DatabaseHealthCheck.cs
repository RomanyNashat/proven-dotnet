using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ClinicReminders.Infrastructure;

/// <summary>Readiness: the pod can serve only if it can reach its database.</summary>
public sealed class DatabaseHealthCheck(RemindersDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Cannot connect to the Reminders database.");
}
