---
name: testing-architecture
description: Architecture tests for .NET with NetArchTest — layer dependencies, handlers sealed and named, entities changed through methods, and a guard so a renamed namespace can't turn a rule into one that never fails. Tested in CI against a well-layered and a deliberately broken sample.
version: 2.0.0
---

# Architecture Testing Patterns

The layer rules in `rules/architecture.md` hold only if something checks them. Architecture tests do,
on every build: a domain class that reaches for the database fails the build with its name in the
message.

## Setup

```xml
<PackageReference Include="NetArchTest.Rules" Version="1.3.2" />
```

NetArchTest has had no release since 1.3.2; it still works on .NET 10 (the samples run on it). For rules
it can't express (method-level dependencies, slices), ArchUnitNET is maintained and Apache-2.0.

## The rules

<!-- sample: tests/SkillSamples.Tests/Architecture/ArchitectureRules.cs -->
```csharp
// The layer namespaces of one service. With one project per layer, they're the projects' root namespaces.
public sealed record Layers(string Domain, string Application, string Infrastructure, string Api);

public static class ArchitectureRules
{
    private static readonly string[] InfrastructurePackages =
        ["Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.Data.SqlClient", "Dapper", "MongoDB.Driver", "StackExchange.Redis", "Confluent.Kafka"];

    public static IReadOnlyList<string> DomainDependsOnNothing(Assembly assembly, Layers layers) =>
        Check(assembly, layers.Domain, types => types
            .ShouldNot().HaveDependencyOnAny([layers.Application, layers.Infrastructure, layers.Api, .. InfrastructurePackages]));

    public static IReadOnlyList<string> ApplicationDependsOnDomainOnly(Assembly assembly, Layers layers) =>
        Check(assembly, layers.Application, types => types
            .ShouldNot().HaveDependencyOnAny([layers.Infrastructure, layers.Api, .. InfrastructurePackages]));

    public static IReadOnlyList<string> ApiGoesThroughTheApplication(Assembly assembly, Layers layers) =>
        Check(assembly, layers.Api, types => types
            .ShouldNot().HaveDependencyOnAny([layers.Infrastructure, .. InfrastructurePackages]));

    public static IReadOnlyList<string> HandlersAreSealedAndNamed(Assembly assembly, Layers layers) =>
    [
        .. Check(assembly, layers.Application, types => types.And().ImplementInterface(typeof(ICommandHandler<,>)).Should().BeSealed()),
        .. Check(assembly, layers.Application, types => types.And().ImplementInterface(typeof(ICommandHandler<,>)).Should().HaveNameEndingWith("Handler"))
    ];

    // NetArchTest doesn't see whether a setter is init-only, so this rule uses reflection.
    public static IReadOnlyList<string> DomainStateChangesThroughMethods(Assembly assembly, Layers layers) =>
        Selected(assembly, layers.Domain).GetTypes()
            .Where(t => t.IsClass && t.GetMethod("<Clone>$") is null)   // records are values: init is fine
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } setter
                            && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

    // A rule that selects no types passes. A renamed namespace would turn every rule into a test that
    // can never fail, so each one first checks it selected something.
    private static PredicateList Selected(Assembly assembly, string layer)
    {
        var selection = Types.InAssembly(assembly).That().ResideInNamespace(layer);
        return selection.GetTypes().Any()
            ? selection
            : throw new InvalidOperationException($"No types in {layer}: is the namespace right?");
    }

    private static IReadOnlyList<string> Check(Assembly assembly, string layer, Func<PredicateList, ConditionList> rule)
    {
        var result = rule(Selected(assembly, layer)).GetResult();
        return result.IsSuccessful ? [] : [.. result.FailingTypes.Select(t => t.FullName!)];
    }
}
```

Each rule returns the names of the types that break it, so a failure reads "OrderPricing", not "expected
true but found false".

- **Layers by namespace.** With one project per layer, pass `typeof(SomeDomainType).Assembly` and the
  project's root namespace; with one project, the namespaces do the same job.
- **Domain depends on nothing:** no other layer, and no data or messaging package.
- **`ImplementInterface` works with an open generic** (`typeof(ICommandHandler<,>)`, tested). It looks at
  the interfaces a type declares itself, not ones it inherits from a base class.
- **The guard against empty selections.** Tested as a story: with a typo in the namespace, NetArchTest
  selects nothing and the rule passes; the guard turns that into a failure.

## Tests

```csharp
[Trait("Category", "Architecture")]
public sealed class ArchitectureTests
{
    private static readonly Assembly Assembly = typeof(Order).Assembly;
    private static readonly Layers Layers = new("Shop.Domain", "Shop.Application", "Shop.Infrastructure", "Shop.Api");

    [Fact]
    public void Layers_FollowEveryRule()
    {
        ArchitectureRules.DomainDependsOnNothing(Assembly, Layers).Should().BeEmpty();
        ArchitectureRules.ApplicationDependsOnDomainOnly(Assembly, Layers).Should().BeEmpty();
        ArchitectureRules.ApiGoesThroughTheApplication(Assembly, Layers).Should().BeEmpty();
        ArchitectureRules.HandlersAreSealedAndNamed(Assembly, Layers).Should().BeEmpty();
        ArchitectureRules.DomainStateChangesThroughMethods(Assembly, Layers).Should().BeEmpty();
    }
}
```

The samples run every rule against two copies of a small service: one laid out properly (every rule
passes) and one with the usual mistakes. Tested as stories, each mistake is caught and named:
- a domain service that takes the `DbContext` (`OrderPricing`);
- an endpoint that queries the database instead of going through the application layer;
- a handler left unsealed, and another that implements the interface without the `Handler` suffix;
- an entity with a public setter (`Invoice.Total`); `init` setters and records pass.

Tested with no ICU: the rules give the same answers.

## Running them

```bash
dotnet test --filter "Category=Architecture"
```

They run with the unit tests on every build; they need no database and take a second or two.

## Rules
- One architecture test project (or class) per service, run on every build.
- Rules return the failing type names; assertions show them.
- Every rule checks its selection isn't empty.
- Layers: Domain → nothing; Application → Domain; Infrastructure → Domain and Application; Api → Application.
