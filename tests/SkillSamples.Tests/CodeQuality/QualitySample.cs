using SkillSamples.MsBuild;

namespace SkillSamples.CodeQuality;

/// <summary>A copy of the code-quality sample in a temporary folder: the skill's props and .editorconfig.</summary>
public sealed class QualitySample : IDisposable
{
    public const string Project = "src/Clinic.Api/Clinic.Api.csproj";

    private static readonly string Source = Path.Combine(AppContext.BaseDirectory, "CodeQuality", "sample");

    public QualitySample()
    {
        Root = Directory.CreateTempSubdirectory("quality-sample-").FullName;
        foreach (var file in Directory.EnumerateFiles(Source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(Root, Path.GetRelativePath(Source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public string Root { get; }

    public QualitySample Write(string relativePath, string text)
    {
        File.WriteAllText(Path.Combine(Root, relativePath), text);
        return this;
    }

    public string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    public Task<DotnetResult> BuildAsync() => Dotnet.RunAsync(Root, "build", Project, "-nologo");

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
