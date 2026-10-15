using SkillSamples.MsBuild;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.CodeQuality;

/// <summary>The skill's Directory.Build.props and .editorconfig, built with the real CLI.</summary>
public sealed class CodeQualityTests
{
    private const string Counter = "src/Clinic.Api/VisitCounter.cs";

    // Follows the house style: file-scoped namespace, _camelCase field, an await with no ConfigureAwait.
    private const string Clean = """
        namespace Clinic.Api;

        public sealed class VisitCounter
        {
            private int _count;

            public int Count => _count;

            public async Task<int> AddAsync(CancellationToken ct)
            {
                await Task.Delay(1, ct);
                return ++_count;
            }
        }
        """;

    private static void Builds(DotnetResult result) => Assert.True(result.Succeeded, Tail(result.Output));

    private static void FailsWith(DotnetResult result, string rule)
    {
        Assert.False(result.Succeeded, "expected the build to fail");
        Assert.Contains($"error {rule}", result.Output);
    }

    private static string Tail(string output) => output.Length <= 3000 ? output : output[^3000..];

    [Fact]
    public async Task CleanCode_Builds()
    {
        using var sample = new QualitySample().Write(Counter, Clean);

        Builds(await sample.BuildAsync());
    }

    [Fact]
    public async Task PrivateFieldWithoutUnderscore_FailsTheBuild()
    {
        using var sample = new QualitySample().Write(Counter, Clean.Replace("_count", "count"));

        FailsWith(await sample.BuildAsync(), "IDE1006");
    }

    [Fact]
    public async Task BlockScopedNamespace_FailsTheBuild()
    {
        var blockScoped = Clean.Replace("namespace Clinic.Api;", "namespace Clinic.Api\n{") + "\n}\n";
        using var sample = new QualitySample().Write(Counter, blockScoped);

        FailsWith(await sample.BuildAsync(), "IDE0161");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_SomeoneDeletesTheCa2007Line_EveryAwaitBreaksTheBuild()
    {
        // Given the house settings: every analyzer on (latest-all), warnings as errors
        using var sample = new QualitySample().Write(Counter, Clean);
        var editorconfig = sample.Read(".editorconfig");

        // When the line that turns CA2007 off is removed ("we don't need that")
        sample.Write(".editorconfig", string.Join('\n', editorconfig.Split('\n').Where(l => !l.Contains("CA2007"))));

        // Then a plain await in ASP.NET Core code fails the build: the line is why it builds
        FailsWith(await sample.BuildAsync(), "CA2007");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_OnASlimBuildAgent_TheRulesHoldTheSame()
    {
        ProductionConditions.Require();
        using var sample = new QualitySample().Write(Counter, Clean.Replace("_count", "count"));

        FailsWith(await sample.BuildAsync(), "IDE1006");
    }
}
