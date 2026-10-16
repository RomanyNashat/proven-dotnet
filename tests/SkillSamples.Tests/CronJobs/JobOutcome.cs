namespace SkillSamples.CronJobs;

/// <summary>What a run-once job reports back to Main, which returns it as the process exit code.</summary>
public sealed class JobOutcome
{
    public int ExitCode { get; private set; }

    public void Failed() => ExitCode = 1;
}
