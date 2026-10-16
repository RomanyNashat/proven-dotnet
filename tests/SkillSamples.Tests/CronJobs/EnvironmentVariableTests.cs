using Microsoft.Extensions.Hosting;
using Xunit;

namespace SkillSamples.CronJobs;

// These tests set process-wide environment variables, so they run alone, after the parallel tests.
[CollectionDefinition(nameof(ProcessEnvironment), DisableParallelization = true)]
public sealed class ProcessEnvironment;

[Collection(nameof(ProcessEnvironment))]
public sealed class EnvironmentVariableTests
{
    private static string EnvironmentNameWith(string variable, string value)
    {
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, value);
        try
        {
            return Host.CreateApplicationBuilder().Environment.EnvironmentName;
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
        }
    }

    [Fact]
    public void ConsoleHost_ReadsDotnetEnvironment()
    {
        Assert.Equal("Staging", EnvironmentNameWith("DOTNET_ENVIRONMENT", "Staging"));
    }

    [Fact]
    public void ConsoleHost_IgnoresAspnetcoreEnvironment_SoAStagingJobRunsAsProduction()
    {
        Assert.Equal(Environments.Production, EnvironmentNameWith("ASPNETCORE_ENVIRONMENT", "Staging"));
    }
}
