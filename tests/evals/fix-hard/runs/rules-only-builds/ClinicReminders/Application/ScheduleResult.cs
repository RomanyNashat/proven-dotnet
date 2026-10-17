namespace ClinicReminders.Application;

public abstract record ScheduleResult
{
    private ScheduleResult()
    {
    }

    public sealed record Scheduled(int Id) : ScheduleResult;

    public sealed record Invalid(IDictionary<string, string[]> Errors) : ScheduleResult;

    public sealed record Forbidden : ScheduleResult;
}
