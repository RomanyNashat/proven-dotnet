using SkillSamples.Production;
using Xunit;

namespace SkillSamples.MsBuild;

/// <summary>
/// Restores the sample solution with the real dotnet CLI and nuget.org, and checks what the skill says the
/// central package file and the shared build props do.
/// </summary>
public sealed class CentralPackagesTests
{
    private const string Pkcs = "System.Security.Cryptography.Pkcs";

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheAuditFailsOnAPackageNobodyReferenced_OneCentralLineFixesEveryProject()
    {
        // Given: two projects that never mention Pkcs. SqlClient brings it in at 9.0.11, which has known
        // vulnerabilities, and the audit treats those as errors
        using var solution = new SampleSolution();

        // When: the solution is restored without transitive pinning
        var unpinned = await solution.RestoreAsync(SampleSolution.TestProject, "CentralPackageTransitivePinningEnabled=false");

        // Then: the restore fails on Pkcs, though no project file names it
        Assert.False(unpinned.Succeeded, unpinned.Output);
        Assert.Matches(@"NU190[1-4].*System\.Security\.Cryptography\.Pkcs", unpinned.Output);

        // When: it is restored as committed, with the one PackageVersion line for Pkcs pinned centrally
        var pinned = await solution.RestoreAsync(SampleSolution.TestProject);

        // Then: it succeeds, and both projects get the fixed version
        Assert.True(pinned.Succeeded, pinned.Output);
        Assert.Equal("10.0.13", solution.ResolvedVersion(SampleSolution.DataProject, Pkcs));
        Assert.Equal("10.0.13", solution.ResolvedVersion(SampleSolution.TestProject, Pkcs));
    }

    [Fact]
    public async Task NuGetWhy_PinnedTransitivePackage_ShowsThePackageThatBroughtItIn()
    {
        using var solution = new SampleSolution();
        Assert.True((await solution.RestoreAsync(SampleSolution.DataProject)).Succeeded);

        var why = await solution.RunAsync("nuget", "why", SampleSolution.DataProject, Pkcs);

        Assert.True(why.Succeeded, why.Output);
        Assert.Contains("Microsoft.Data.SqlClient", why.Output);
        Assert.Contains("10.0.13", why.Output);
    }

    [Fact]
    public async Task Restore_ProjectSetsItsOwnVersion_FailsWithNU1008()
    {
        using var solution = new SampleSolution();
        var project = solution.AddProject("tests/Clinic.Contract.Tests/Clinic.Contract.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="FluentAssertions" Version="8.0.0" />
              </ItemGroup>
            </Project>
            """);

        var restore = await solution.RestoreAsync(project);

        Assert.False(restore.Succeeded);
        Assert.Contains("NU1008", restore.Output);
    }

    [Fact]
    public async Task Restore_ProjectOverridesTheCentralVersion_FailsWithNU1013()
    {
        using var solution = new SampleSolution();
        var project = solution.AddProject("tests/Clinic.Contract.Tests/Clinic.Contract.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="FluentAssertions" VersionOverride="8.0.0" />
              </ItemGroup>
            </Project>
            """);

        var restore = await solution.RestoreAsync(project);

        Assert.False(restore.Succeeded);
        Assert.Contains("NU1013", restore.Output);
    }

    [Fact]
    public async Task Restore_RangeInTheCentralFile_TakesTheLowestVersionInIt()
    {
        using var solution = new SampleSolution();

        Assert.True((await solution.RestoreAsync(SampleSolution.TestProject)).Succeeded);

        Assert.Equal("7.0.0", solution.ResolvedVersion(SampleSolution.TestProject, "FluentAssertions"));
    }

    [Fact]
    public async Task NestedBuildProps_ImportsTheRoot_SoTestProjectsKeepTheSharedSettings()
    {
        using var solution = new SampleSolution();

        var tests = await solution.PropertiesAsync(SampleSolution.TestProject, "IsPackable", "Nullable", "NuGetAuditMode", "ManagePackageVersionsCentrally");
        var data = await solution.PropertiesAsync(SampleSolution.DataProject, "IsPackable", "Nullable", "NuGetAuditMode");

        Assert.Equal(("false", "enable", "all", "true"),
            (tests["IsPackable"], tests["Nullable"], tests["NuGetAuditMode"], tests["ManagePackageVersionsCentrally"]));
        Assert.Equal(("true", "enable", "all"), (data["IsPackable"], data["Nullable"], data["NuGetAuditMode"]));
    }

    [Fact]
    public async Task NestedBuildProps_WithoutTheImport_SilentlyDropsTheSharedSettings()
    {
        using var solution = new SampleSolution();
        // The same folder file, written the way it usually is the first time: no import of the root.
        File.WriteAllText(Path.Combine(solution.Root, "tests", "Directory.Build.props"), """
            <Project>
              <PropertyGroup>
                <IsPackable>false</IsPackable>
              </PropertyGroup>
            </Project>
            """);

        var tests = await solution.PropertiesAsync(SampleSolution.TestProject, "IsPackable", "Nullable", "TreatWarningsAsErrors");

        // No error and no warning: the test projects just stop treating warnings as errors.
        Assert.Equal("false", tests["IsPackable"]);
        Assert.NotEqual("enable", tests["Nullable"]);
        Assert.NotEqual("true", tests["TreatWarningsAsErrors"]);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_RestoreAuditsAndPinsTheSame()
    {
        // A slim build image: the CLI runs without ICU and without time zone data.
        ProductionConditions.Require();
        using var solution = new SampleSolution();

        var restore = await solution.RestoreAsync(SampleSolution.TestProject);

        Assert.True(restore.Succeeded, restore.Output);
        Assert.Equal("10.0.13", solution.ResolvedVersion(SampleSolution.DataProject, Pkcs));
        Assert.Equal("7.0.0", solution.ResolvedVersion(SampleSolution.TestProject, "FluentAssertions"));
    }
}
