using System.Diagnostics;

namespace SkillSamples.MsBuild;

public sealed record DotnetResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs the dotnet CLI the way a developer would, and returns everything it printed.</summary>
public static class Dotnet
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(4);

    public static async Task<DotnetResult> RunAsync(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        // The test host runs inside an MSBuild-aware process; its MSBuild variables would point the child at
        // the wrong SDK files. Build nodes that outlive the command would also keep the output pipes open.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(key);
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet didn't start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} took longer than {Timeout}");
        }

        return new DotnetResult(process.ExitCode, await output + await error);
    }
}
