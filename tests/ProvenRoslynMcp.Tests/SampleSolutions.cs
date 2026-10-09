using System.Diagnostics;

namespace ProvenRoslynMcp.Tests;

/// <summary>
/// Builds small throwaway solutions once per test run: a healthy two-project solution (.sln and .slnx),
/// a copy whose design-time build never finishes, and a folder holding two solutions.
/// </summary>
public sealed class SampleSolutions : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("proven-roslyn-tests-").FullName;
    public string HealthySln { get; }
    public string Slnx { get; }
    public string StallingSln { get; }
    public string SeveralDir { get; }

    public SampleSolutions()
    {
        var healthy = Path.Combine(Root, "healthy");
        WriteProjects(healthy, stall: false);
        Dotnet(healthy, "new", "sln", "-n", "Sample", "--format", "sln");
        Dotnet(healthy, "sln", "Sample.sln", "add", "Lib/Lib.csproj", "Api/Api.csproj");
        Dotnet(healthy, "restore", "Sample.sln");
        HealthySln = Path.Combine(healthy, "Sample.sln");

        File.WriteAllText(Path.Combine(healthy, "Sample.slnx"),
            "<Solution>\n  <Project Path=\"Api/Api.csproj\" />\n  <Project Path=\"Lib/Lib.csproj\" />\n</Solution>\n");
        Slnx = Path.Combine(healthy, "Sample.slnx");

        var stalling = Path.Combine(Root, "stalling");
        WriteProjects(stalling, stall: true);
        Dotnet(stalling, "new", "sln", "-n", "Sample", "--format", "sln");
        Dotnet(stalling, "sln", "Sample.sln", "add", "Lib/Lib.csproj", "Api/Api.csproj");
        Dotnet(stalling, "restore", "Sample.sln");
        StallingSln = Path.Combine(stalling, "Sample.sln");

        SeveralDir = Path.Combine(Root, "several");
        foreach (var sub in new[] { "a", "b" })
        {
            Directory.CreateDirectory(Path.Combine(SeveralDir, sub));
            File.Copy(HealthySln, Path.Combine(SeveralDir, sub, "svc.sln"));
        }
    }

    private static void WriteProjects(string dir, bool stall)
    {
        const string props = "<PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>";
        // A design-time build step that outlasts the load limit — what a network share or a credential prompt
        // looks like. Bounded (45s, the test's limit is 15s+5s) so the orphaned process ends by itself: an
        // endless one keeps the test runner's output pipe open and `dotnet test` never exits.
        var stallTarget = stall ? "<Target Name=\"Stall\" BeforeTargets=\"CoreCompile\"><Exec Command=\"sleep 45\" /></Target>" : "";

        Directory.CreateDirectory(Path.Combine(dir, "Lib"));
        File.WriteAllText(Path.Combine(dir, "Lib", "Lib.csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\">{props}{stallTarget}</Project>");
        File.WriteAllText(Path.Combine(dir, "Lib", "OrderService.cs"),
            "namespace Lib;\npublic class OrderService\n{\n    public int Total(int a, int b) => a + b;\n}\n");

        Directory.CreateDirectory(Path.Combine(dir, "Api"));
        File.WriteAllText(Path.Combine(dir, "Api", "Api.csproj"),
            $"<Project Sdk=\"Microsoft.NET.Sdk\">{props}<ItemGroup><ProjectReference Include=\"../Lib/Lib.csproj\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(dir, "Api", "Endpoint.cs"),
            "namespace Api;\npublic class Endpoint\n{\n    private readonly Lib.OrderService _svc = new();\n    public int Handle() => _svc.Total(1, 2);\n    public int Again() => _svc.Total(3, 4);\n}\n");
    }

    private static void Dotnet(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("dotnet") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {string.Join(' ', args)} failed:\n{stdout}\n{stderr}");
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* a stuck BuildHost may still hold files */ }
    }
}
