using System.Diagnostics;
using Xunit;

namespace SkillSamples.Mongo;

/// <summary>
/// Runs a script from scripts/ with mongosh inside the CI mongo container, which mounts that folder at
/// /scripts: the same file the skill shows is the one that runs.
/// </summary>
internal static class MongoShell
{
    public static async Task RunAsync(string database, string script)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[]
        {
            "exec", Environment.GetEnvironmentVariable("MONGO_CONTAINER") ?? "mongo",
            "mongosh", "--quiet", $"mongodb://localhost:27017/{database}?directConnection=true", $"/scripts/{script}",
        })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, $"mongosh {script} exited {process.ExitCode}: {await output} {await error}");
    }
}
