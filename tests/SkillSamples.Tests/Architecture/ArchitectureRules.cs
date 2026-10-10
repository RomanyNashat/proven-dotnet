using System.Reflection;
using System.Runtime.CompilerServices;
using NetArchTest.Rules;

namespace SkillSamples.Architecture;

// The samples' own command contracts. (The CQRS samples register every ICommandHandler they find in this
// assembly, so these layers can't use theirs.)
public interface ICommand<TResult>;

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}

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
