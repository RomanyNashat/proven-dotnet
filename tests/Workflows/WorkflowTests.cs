using SkillSamples.Production;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace SkillSamples.Workflows;

/// <summary>
/// What the cicd-github-actions workflows decide, read from the files the skill shows. actionlint (in CI)
/// proves they are valid; these prove they do what the skill says.
/// </summary>
public sealed class WorkflowTests
{
    private static YamlMappingNode Load(string file)
    {
        var yaml = new YamlStream();
        using var reader = new StreamReader(Path.Combine(AppContext.BaseDirectory, "Workflows", file));
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    private static YamlMappingNode Map(YamlMappingNode node, string key) => (YamlMappingNode)node.Children[new YamlScalarNode(key)];

    private static bool Has(YamlMappingNode node, string key) => node.Children.ContainsKey(new YamlScalarNode(key));

    private static string Text(YamlMappingNode node, string key) =>
        Has(node, key) ? ((YamlScalarNode)node.Children[new YamlScalarNode(key)]).Value ?? "" : "";

    private static List<YamlMappingNode> Steps(YamlMappingNode job) =>
        [.. ((YamlSequenceNode)job.Children[new YamlScalarNode("steps")]).Children.Cast<YamlMappingNode>()];

    private static Dictionary<string, string> Run(string eventName, string gitRef, string? environment = null)
    {
        var context = new Dictionary<string, string> { ["github.event_name"] = eventName, ["github.ref"] = gitRef };
        if (environment is not null)
        {
            context["inputs.environment"] = environment;
            context["github.event.inputs.environment"] = environment;
        }

        return context;
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_SomeoneDeploysToStagingByHandFromMain_ProductionIsLeftAlone()
    {
        // Given: the deploy workflow, and a developer who runs it by hand from main and picks "staging" to
        // try a fix there first
        var production = Map(Map(Load("deploy.yml"), "jobs"), "deploy-production");
        var condition = Text(production, "if");
        var stagingByHand = Run("workflow_dispatch", "refs/heads/main", environment: "staging");

        // When: GitHub decides whether the production job runs
        var runs = WorkflowExpression.IsTrue(condition, stagingByHand);

        // Then: it doesn't. The condition this replaced also matched "any run from main", so the staging
        // choice went to production too, held back only by the environment's reviewers, if it had any
        Assert.False(runs);
        const string Replaced = "github.event.inputs.environment == 'production' || github.ref == 'refs/heads/main'";
        Assert.True(WorkflowExpression.IsTrue(Replaced, stagingByHand));
    }

    [Theory]
    [InlineData("push", "refs/heads/main", null, false)]
    [InlineData("workflow_dispatch", "refs/heads/main", "production", true)]
    [InlineData("workflow_dispatch", "refs/heads/release", "production", true)]
    [InlineData("workflow_dispatch", "refs/heads/main", "staging", false)]
    public void DeployProduction_OnlyAManualRunThatChoseProduction(string eventName, string gitRef, string? environment, bool expected)
    {
        var production = Map(Map(Load("deploy.yml"), "jobs"), "deploy-production");

        Assert.Equal(expected, WorkflowExpression.IsTrue(Text(production, "if"), Run(eventName, gitRef, environment)));
    }

    [Fact]
    public void DeployedImageTag_IsTheCommit_SoEveryDeployChangesTheImage()
    {
        var jobs = Map(Load("deploy.yml"), "jobs");
        var build = Map(jobs, "build-and-push");
        var meta = Steps(build).Single(s => Text(s, "id") == "meta");

        Assert.Equal("${{ github.sha }}", Text(Map(build, "outputs"), "image-tag"));
        Assert.Contains("type=sha,prefix=,format=long", Text(Map(meta, "with"), "tags"));
        foreach (var deploy in new[] { "deploy-staging", "deploy-production" })
        {
            var deploys = Steps(Map(jobs, deploy)).Select(s => Text(s, "run")).Where(r => r.Contains("kubectl set image")).ToList();
            Assert.NotEmpty(deploys);
            Assert.All(deploys, r => Assert.Contains("needs.build-and-push.outputs.image-tag", r));
        }
    }

    [Fact]
    public void Permissions_EachJobGetsOnlyWhatItUses()
    {
        var workflow = Load("deploy.yml");
        var jobs = Map(workflow, "jobs");
        string Permissions(YamlMappingNode node) =>
            string.Join(",", Map(node, "permissions").Children.Select(p => $"{((YamlScalarNode)p.Key).Value}={((YamlScalarNode)p.Value).Value}").Order());

        Assert.Equal("contents=read", Permissions(workflow));
        Assert.Equal("contents=read,packages=write", Permissions(Map(jobs, "build-and-push")));
        Assert.Equal("contents=read,id-token=write", Permissions(Map(jobs, "deploy-staging")));
        Assert.Equal("contents=read,id-token=write", Permissions(Map(jobs, "deploy-production")));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_ThePrPipelineRunsProductionTests_UnderTheConditionsThisTestRunsUnder()
    {
        // This test runs in our own CI's production step. The sample pipeline's step must recreate the same
        // conditions, or a service's production tests would pass on a runner that has ICU and tzdata.
        ProductionConditions.Require();
        var steps = Steps(Map(Map(Load("pr-validation.yml"), "jobs"), "build-and-test"));
        var production = steps.Single(s => Text(s, "run").Contains("Category=Production"));
        var unit = steps.Single(s => Text(s, "name") == "Unit Tests");

        foreach (var name in new[] { "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "TZDIR", "TZ" })
            Assert.Equal(Environment.GetEnvironmentVariable(name), Text(Map(production, "env"), name));
        // The ordinary run leaves them out: they'd fail there, by design, on a runner with ICU and tzdata.
        Assert.Contains("Category!=Production", Text(unit, "run"));
    }
}
