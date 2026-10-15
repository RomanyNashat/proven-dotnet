using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SkillSamples.CronJobs;

public static class DailyReportHost
{
    /// <summary>Program.cs is <c>return await DailyReportHost.RunAsync(args, ...)</c>: the exit code Kubernetes reads.</summary>
    public static async Task<int> RunAsync(string[] args, Action<HostApplicationBuilder> configure)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton<JobOutcome>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddHostedService<DailyReportJob>();
        configure(builder);   // the report service and its database

        using var host = builder.Build();
        await host.RunAsync();
        return host.Services.GetRequiredService<JobOutcome>().ExitCode;
    }
}
