using ProvenRoslynMcp.Tools;
using ProvenRoslynMcp.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ProvenRoslynMcp.Tests;

/// <summary>get_package_api answers "how do I call this library" from metadata, so nothing gets decompiled.</summary>
public sealed class PackageApiTests(SampleSolutions samples) : IClassFixture<SampleSolutions>
{
    private SolutionManager Manager()
    {
        Environment.SetEnvironmentVariable("PROVEN_SOLUTION_PATH", samples.HealthySln);
        Environment.SetEnvironmentVariable("PROVEN_SOLUTION_LOAD_TIMEOUT_SECONDS", "120");
        Environment.SetEnvironmentVariable("PROVEN_READY_WAIT_SECONDS", "90");
        return new SolutionManager(NullLogger<SolutionManager>.Instance);
    }

    [Fact]
    public async Task ReferencedAssembly_ListsTheTypeAndItsMemberSignatures()
    {
        using var client = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var answer = await PackageTools.GetPackageApi(Manager(), "System.Text.Json", "JsonSerializer", 5, client.Token);

        Assert.Contains("\"name\":\"System.Text.Json.JsonSerializer\"", answer);
        Assert.Contains("JsonSerializer.Serialize<", answer);
        Assert.Contains("JsonSerializer.Deserialize<", answer);
        Assert.DoesNotContain("\"found\":false", answer);
    }

    [Fact]
    public async Task NotReferenced_SaysSo_InsteadOfGuessing()
    {
        using var client = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var answer = await PackageTools.GetPackageApi(Manager(), "Newtonsoft.Json", null, 5, client.Token);

        Assert.Contains("\"found\":false", answer);
        Assert.Contains("must be referenced", answer);
    }

    [Fact]
    public async Task InternalTypes_StayOut()
    {
        using var client = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var answer = await PackageTools.GetPackageApi(Manager(), "System.Text.Json", "ThrowHelper", 5, client.Token);

        Assert.Contains("\"count\":0", answer);
    }
}
