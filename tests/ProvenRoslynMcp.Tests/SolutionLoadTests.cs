using System.Diagnostics;
using ProvenRoslynMcp.Tools;
using ProvenRoslynMcp.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ProvenRoslynMcp.Tests;

/// <summary>
/// Regression tests for load failures seen in real use. Each test names the bug it guards.
/// Tests run one at a time (xunit.runner.json): they set process-wide environment variables and
/// the current directory.
/// </summary>
public sealed class SolutionLoadTests(SampleSolutions samples) : IClassFixture<SampleSolutions>
{
    private static SolutionManager NewManager(string? solution, int loadTimeout = 120, int readyWait = 90)
    {
        Environment.SetEnvironmentVariable("PROVEN_SOLUTION_PATH", solution);
        Environment.SetEnvironmentVariable("PROVEN_SOLUTION_LOAD_TIMEOUT_SECONDS", loadTimeout.ToString());
        Environment.SetEnvironmentVariable("PROVEN_READY_WAIT_SECONDS", readyWait.ToString());
        Environment.SetEnvironmentVariable("PROVEN_RETRY_AFTER_SECONDS", "60");
        return new SolutionManager(NullLogger<SolutionManager>.Instance);
    }

    /// An earlier background warm-up awaited itself, so every call hung until the client gave up.
    [Fact]
    public async Task Healthy_ConcurrentCallsDuringWarmUp_AllAnswer()
    {
        var manager = NewManager(samples.HealthySln);
        manager.StartWarmUp();                               // exactly what Program.cs does after the handshake
        using var client = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var clock = Stopwatch.StartNew();

        var answers = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => SymbolTools.FindReferences(manager, "OrderService", client.Token)));

        Assert.All(answers, a => Assert.Contains("\"count\":2", a));
        Assert.True(clock.Elapsed < TimeSpan.FromMinutes(2), $"took {clock.Elapsed}");

        var warm = await SymbolTools.FindCallers(manager, "Total", client.Token);
        Assert.Contains("Api.Endpoint.Handle()", warm);
        Assert.Contains("\"state\":\"ready\"", WorkspaceTools.WorkspaceStatus(manager));
    }

    /// A design-time build that never returns must turn into a fast "loading" answer, then a clear error
    /// naming the project — never a silent wait.
    [Fact]
    public async Task StallingLoad_AnswersLoading_ThenNamesTheStuckProject()
    {
        if (OperatingSystem.IsWindows())
        {
            return;                                           // the stall uses `sleep`; covered on Linux
        }

        var manager = NewManager(samples.StallingSln, loadTimeout: 15, readyWait: 5);
        manager.StartWarmUp();

        var clock = Stopwatch.StartNew();
        var first = await SymbolTools.FindReferences(manager, "OrderService", CancellationToken.None);
        Assert.Contains("\"status\":\"loading\"", first);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"'loading' took {clock.Elapsed}");

        await Task.Delay(TimeSpan.FromSeconds(25));          // past the 15s (+5s grace) hard limit
        var later = await SymbolTools.FindReferences(manager, "OrderService", CancellationToken.None);
        Assert.Contains("\"status\":\"error\"", later);
        Assert.Contains("Lib.csproj", later);
        Assert.Contains("\"state\":\"failed\"", WorkspaceTools.WorkspaceStatus(manager));
    }

    /// Roslyn 4.14 could not read .slnx ("No file format header found").
    [Fact]
    public async Task Slnx_Loads()
    {
        var manager = NewManager(samples.Slnx);
        using var client = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        var answer = await SymbolTools.FindReferences(manager, "OrderService", client.Token);

        Assert.Contains("\"count\":2", answer);
    }

    /// With several solutions under the folder, the error lists them and says what to do.
    [Fact]
    public async Task SeveralSolutions_ErrorListsThem()
    {
        var previous = Directory.GetCurrentDirectory();
        try
        {
            var manager = NewManager(solution: null);
            Directory.SetCurrentDirectory(samples.SeveralDir);

            var answer = await SymbolTools.FindSymbol(manager, "OrderService", CancellationToken.None);

            Assert.Contains("\"status\":\"error\"", answer);
            Assert.Contains("Found 2", answer);
            Assert.Contains("svc.sln", answer);
            Assert.Contains("PROVEN_SOLUTION_PATH", answer);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }
}
