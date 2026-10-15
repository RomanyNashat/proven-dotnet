namespace SkillSamples.CronJobs;

public interface IReportService
{
    Task<int> GenerateDailyReportAsync(DateOnly day, CancellationToken ct);
}
