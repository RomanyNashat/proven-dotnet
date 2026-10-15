using System.Text.Json;

namespace SkillSamples.MsBuild;

/// <summary>
/// A fresh copy of the sample solution in a temporary folder, so each test restores its own and nothing it
/// writes (obj folders, extra projects) lands in the repo.
/// </summary>
public sealed class SampleSolution : IDisposable
{
    public const string DataProject = "src/Clinic.Data/Clinic.Data.csproj";
    public const string TestProject = "tests/Clinic.Tests/Clinic.Tests.csproj";

    private static readonly string Source = Path.Combine(AppContext.BaseDirectory, "MsBuild");

    public SampleSolution()
    {
        Root = Directory.CreateTempSubdirectory("msbuild-sample-").FullName;
        foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(Source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public string Root { get; }

    public Task<DotnetResult> RestoreAsync(string project, params string[] properties) =>
        Dotnet.RunAsync(Root, ["restore", project, .. properties.Select(p => $"-p:{p}")]);

    public Task<DotnetResult> RunAsync(params string[] arguments) => Dotnet.RunAsync(Root, arguments);

    /// <summary>Adds a project to the solution, as a developer adding a new service would.</summary>
    public string AddProject(string relativePath, string xml)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, xml);
        return relativePath;
    }

    /// <summary>Replaces the root Directory.Build.props, to try a variant of it.</summary>
    public void WriteRootBuildProps(string xml) => File.WriteAllText(Path.Combine(Root, "Directory.Build.props"), xml);

    /// <summary>The version of a package that restore chose for a project, read from its assets file.</summary>
    public string? ResolvedVersion(string project, string packageId)
    {
        // UseArtifactsOutput: obj is artifacts/obj/<project name>/, not next to the project file.
        var assets = Path.Combine(Root, "artifacts", "obj", Path.GetFileNameWithoutExtension(project), "project.assets.json");
        using var json = JsonDocument.Parse(File.ReadAllText(assets));
        var prefix = packageId + "/";
        return json.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(library => library.Name)
            .Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => name[prefix.Length..])
            .SingleOrDefault();
    }

    /// <summary>Property values as MSBuild evaluates them for a project, after every imported file.</summary>
    public async Task<IReadOnlyDictionary<string, string>> PropertiesAsync(string project, params string[] names)
    {
        var result = await RunAsync(["msbuild", project, .. names.Select(n => $"-getProperty:{n}")]);
        if (!result.Succeeded) throw new InvalidOperationException(result.Output);
        // With more than one property, MSBuild prints them as JSON: {"Properties": {"Name": "value"}}.
        using var json = JsonDocument.Parse(result.Output[result.Output.IndexOf('{')..]);
        return json.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* A temp folder left behind is harmless; the test result is what matters. */ }
    }
}
