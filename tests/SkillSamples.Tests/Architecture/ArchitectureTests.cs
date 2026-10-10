using FluentAssertions;
using NetArchTest.Rules;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Architecture;

[Trait("Category", "Architecture")]
public sealed class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Assembly = typeof(ArchitectureRules).Assembly;

    private static Layers Of(string service) => new(
        $"SkillSamples.Architecture.{service}.Domain",
        $"SkillSamples.Architecture.{service}.Application",
        $"SkillSamples.Architecture.{service}.Infrastructure",
        $"SkillSamples.Architecture.{service}.Api");

    private static readonly Layers Shop = Of("Shop");
    private static readonly Layers Broken = Of("Broken");

    [Fact]
    public void Shop_FollowsEveryRule()
    {
        ArchitectureRules.DomainDependsOnNothing(Assembly, Shop).Should().BeEmpty();
        ArchitectureRules.ApplicationDependsOnDomainOnly(Assembly, Shop).Should().BeEmpty();
        ArchitectureRules.ApiGoesThroughTheApplication(Assembly, Shop).Should().BeEmpty();
        ArchitectureRules.HandlersAreSealedAndNamed(Assembly, Shop).Should().BeEmpty();
        ArchitectureRules.DomainStateChangesThroughMethods(Assembly, Shop).Should().BeEmpty();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_SomeoneGivesADomainServiceTheDbContext_TheTestNamesTheType()
    {
        ArchitectureRules.DomainDependsOnNothing(Assembly, Broken)
            .Should().Equal("SkillSamples.Architecture.Broken.Domain.OrderPricing");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_AnEndpointQueriesTheDatabaseDirectly_TheTestNamesIt()
    {
        ArchitectureRules.ApiGoesThroughTheApplication(Assembly, Broken)
            .Should().Equal("SkillSamples.Architecture.Broken.Api.InvoiceEndpoints");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_AHandlerIsLeftOpenAndAnotherMisnamed_BothAreCaught()
    {
        ArchitectureRules.HandlersAreSealedAndNamed(Assembly, Broken).Should().BeEquivalentTo(
            "SkillSamples.Architecture.Broken.Application.PingHandler",
            "SkillSamples.Architecture.Broken.Application.PingProcessor");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_AnEntityGetsAPublicSetter_TheTestNamesTheProperty()
    {
        ArchitectureRules.DomainStateChangesThroughMethods(Assembly, Broken)
            .Should().Equal("SkillSamples.Architecture.Broken.Domain.Invoice.Total");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Story_TheDomainNamespaceIsRenamed_ARuleWithoutTheGuardWouldPassForever()
    {
        // Given a typo in the namespace, NetArchTest selects nothing, and nothing violates the rule
        var unguarded = Types.InAssembly(Assembly).That().ResideInNamespace("SkillSamples.Architecture.Broken.Domian")
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore").GetResult();
        unguarded.IsSuccessful.Should().BeTrue();

        // With the guard, the same mistake fails the test
        var guarded = () => ArchitectureRules.DomainDependsOnNothing(Assembly, Broken with { Domain = "SkillSamples.Architecture.Broken.Domian" });
        guarded.Should().Throw<InvalidOperationException>().WithMessage("No types in *Domian*");
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Production_NoIcu_TheRulesGiveTheSameAnswers()
    {
        ProductionConditions.Require();

        ArchitectureRules.DomainDependsOnNothing(Assembly, Shop).Should().BeEmpty();
        ArchitectureRules.DomainDependsOnNothing(Assembly, Broken).Should().ContainSingle();
        ArchitectureRules.HandlersAreSealedAndNamed(Assembly, Broken).Should().HaveCount(2);
    }
}
