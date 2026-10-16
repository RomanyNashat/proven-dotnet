using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillSamples.Localization;

namespace SkillSamples.CronJobs;

public sealed class DailyReportJob(
    IServiceScopeFactory scopeFactory,
    IHostApplicationLifetime lifetime,
    JobOutcome outcome,
    TimeProvider time,
    ILogger<DailyReportJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var day = ReportDay.Yesterday(time, RiyadhTime.Zone);
            await using var scope = scopeFactory.CreateAsyncScope();
            var records = await scope.ServiceProvider.GetRequiredService<IReportService>()
                .GenerateDailyReportAsync(day, stoppingToken);
            logger.LogInformation("Daily report for {Day}: {Count} records", day, records);
        }
        catch (Exception ex)
        {
            // Includes a SIGTERM mid-run: the report isn't done, so the run failed and Kubernetes retries it.
            logger.LogCritical(ex, "Daily report failed");
            outcome.Failed();
        }
        finally
        {
            lifetime.StopApplication();   // run once: the host stops, Main returns the exit code
        }
    }
}
