---
name: testing-architecture
description: Architecture tests for .NET: enforce layer boundaries, naming, dependency rules (NetArchTest/ArchUnitNET).
version: 1.0.0
---

# Architecture Testing Patterns

## Setup

```xml
<PackageReference Include="NetArchTest.Rules" Version="1.4.0" />
<!-- Or the newer fork: -->
<PackageReference Include="TngTech.ArchUnitNET.xUnit" Version="0.13.0" />
```

## Layer Dependency Tests

```csharp
[Trait("Category", "Architecture")]
public sealed class LayerDependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(Order).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(CreateOrderCommand).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(AppDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOn_Application()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApplicationAssembly.GetName().Name)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FormatFailures("Domain depends on Application", result));
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureAssembly.GetName().Name)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FormatFailures("Domain depends on Infrastructure", result));
    }

    [Fact]
    public void Domain_ShouldNotDependOn_Api()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiAssembly.GetName().Name)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FormatFailures("Domain depends on API", result));
    }

    [Fact]
    public void Application_ShouldNotDependOn_Infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureAssembly.GetName().Name)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FormatFailures("Application depends on Infrastructure", result));
    }

    [Fact]
    public void Application_ShouldNotDependOn_Api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiAssembly.GetName().Name)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FormatFailures("Application depends on API", result));
    }

    [Fact]
    public void Domain_ShouldNotReference_ExternalPackages()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Dapper",
                "MediatR",
                "FluentValidation",
                "Confluent.Kafka",
                "StackExchange.Redis",
                "MongoDB.Driver")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FormatFailures("Domain references external packages", result));
    }

    private static string FormatFailures(string rule, TestResult result)
    {
        if (result.IsSuccessful) return string.Empty;
        var types = string.Join("\n  ", result.FailingTypes?.Select(t => t.FullName) ?? []);
        return $"{rule}. Violating types:\n  {types}";
    }
}
```

## Naming Convention Tests

```csharp
[Trait("Category", "Architecture")]
public sealed class NamingConventionTests
{
    [Fact]
    public void CommandHandlers_ShouldEndWith_Handler()
    {
        var result = Types.InAssembly(typeof(CreateOrderHandler).Assembly)
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Should()
            .HaveNameEndingWith("Handler")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            FormatFailures("Command/Query handlers must end with 'Handler'", result));
    }

    [Fact]
    public void Validators_ShouldEndWith_Validator()
    {
        var result = Types.InAssembly(typeof(CreateOrderCommandValidator).Assembly)
            .That()
            .Inherit(typeof(AbstractValidator<>))
            .Should()
            .HaveNameEndingWith("Validator")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Repositories_ShouldEndWith_Repository()
    {
        var result = Types.InAssembly(typeof(AppDbContext).Assembly)
            .That()
            .HaveNameEndingWith("Repository")
            .Should()
            .ImplementInterface(typeof(IOrderRepository).Assembly
                .GetTypes()
                .First(t => t.Name.EndsWith("Repository") && t.IsInterface))
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Interfaces_ShouldStartWith_I()
    {
        var result = Types.InAssembly(typeof(Order).Assembly)
            .That()
            .AreInterfaces()
            .Should()
            .HaveNameStartingWith("I")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
```

## Design Constraint Tests

```csharp
[Trait("Category", "Architecture")]
public sealed class DesignConstraintTests
{
    [Fact]
    public void Handlers_ShouldBe_Sealed()
    {
        var result = Types.InAssembly(typeof(CreateOrderHandler).Assembly)
            .That()
            .HaveNameEndingWith("Handler")
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void DomainEntities_ShouldNotHave_PublicSetters()
    {
        var entityTypes = Types.InAssembly(typeof(Order).Assembly)
            .That()
            .Inherit(typeof(Entity<>))
            .GetTypes();

        foreach (var type in entityTypes)
        {
            var publicSetters = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true }
                    && p.Name != "Id"  // Id with protected init is OK
                    && p.SetMethod.ReturnParameter?.GetRequiredCustomModifiers()
                        .All(m => m != typeof(System.Runtime.CompilerServices.IsExternalInit)) == true);

            publicSetters.Should().BeEmpty(
                $"{type.Name} has public setters: {string.Join(", ", publicSetters.Select(p => p.Name))}. " +
                "Domain entities should use methods for state changes.");
        }
    }

    [Fact]
    public void ValueObjects_ShouldBe_Records()
    {
        // Convention: types in Domain/ValueObjects namespace should be records
        var valueObjectTypes = Types.InAssembly(typeof(Money).Assembly)
            .That()
            .ResideInNamespaceEndingWith("ValueObjects")
            .GetTypes();

        foreach (var type in valueObjectTypes)
        {
            var isRecord = type.GetMethod("<Clone>$") is not null;
            isRecord.Should().BeTrue($"{type.Name} in ValueObjects should be a record type");
        }
    }

    [Fact]
    public void Controllers_ShouldNotAccess_DbContext_Directly()
    {
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That()
            .HaveNameEndingWith("Endpoints")
            .Or()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "API layer should not access DbContext directly — use Application layer");
    }
}
```

## Running Architecture Tests

```bash
# Run only architecture tests
dotnet test --filter "Category=Architecture"

# Include in CI pipeline as a quality gate
dotnet test --filter "Category=Architecture" --logger "trx;LogFileName=arch-tests.trx"
```
