using Xunit;

namespace SkillSamples.MsBuild;

/// <summary>Where each project's bin and obj end up, as MSBuild evaluates it for the sample solution.</summary>
public sealed class BuildOutputTests
{
    private const string ObjPath = "BaseIntermediateOutputPath";
    private const string BinPath = "BaseOutputPath";

    [Fact]
    public async Task ArtifactsOutput_EachProjectGetsItsOwnFolders_UnderOneArtifactsRoot()
    {
        using var solution = new SampleSolution();

        var data = await solution.PropertiesAsync(SampleSolution.DataProject, ObjPath, BinPath);
        var tests = await solution.PropertiesAsync(SampleSolution.TestProject, ObjPath, BinPath);

        Assert.StartsWith(Normalize(solution.Root, "artifacts", "obj", "Clinic.Data"), Normalize(data[ObjPath]));
        Assert.StartsWith(Normalize(solution.Root, "artifacts", "obj", "Clinic.Tests"), Normalize(tests[ObjPath]));
        Assert.StartsWith(Normalize(solution.Root, "artifacts", "bin", "Clinic.Data"), Normalize(data[BinPath]));
        Assert.StartsWith(Normalize(solution.Root, "artifacts", "bin", "Clinic.Tests"), Normalize(tests[BinPath]));
    }

    [Fact]
    public async Task ArtifactsPathLeftToItsDefault_FollowsTheNearestBuildProps_SoTestsGetTheirOwnArtifactsFolder()
    {
        using var solution = new SampleSolution();
        var props = File.ReadAllText(Path.Combine(solution.Root, "Directory.Build.props"));
        solution.WriteRootBuildProps(props.Replace("<ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>", ""));

        var tests = await solution.PropertiesAsync(SampleSolution.TestProject, ObjPath, "UseArtifactsOutput");

        Assert.Equal("true", tests["UseArtifactsOutput"]);
        Assert.StartsWith(Normalize(solution.Root, "tests", "artifacts", "obj"), Normalize(tests[ObjPath]));
    }

    [Fact]
    public async Task SharedObjAndBin_InBuildProps_PutsEveryProjectInTheSameFolders()
    {
        using var solution = new SampleSolution();
        // A common way to "tidy up" outputs, and the clash: one obj for every project means one
        // project.assets.json, so each restore overwrites the last project's.
        solution.WriteRootBuildProps("""
            <Project>
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)obj/</BaseIntermediateOutputPath>
                <BaseOutputPath>$(MSBuildThisFileDirectory)bin/</BaseOutputPath>
              </PropertyGroup>
            </Project>
            """);

        var data = await solution.PropertiesAsync(SampleSolution.DataProject, ObjPath, BinPath);
        var tests = await solution.PropertiesAsync(SampleSolution.TestProject, ObjPath, BinPath);

        Assert.Equal(Normalize(data[ObjPath]), Normalize(tests[ObjPath]));
        Assert.Equal(Normalize(data[BinPath]), Normalize(tests[BinPath]));
    }

    private static string Normalize(params string[] parts) =>
        Path.GetFullPath(Path.Combine(parts)).Replace('\\', '/').TrimEnd('/') + "/";
}
