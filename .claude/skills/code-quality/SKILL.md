---
name: code-quality
description: Code quality gates for .NET: Roslyn analyzers (Roslynator/Meziantou/.NET), warnings-as-errors, format verification, architecture tests.
version: 1.0.0
---

# Code Quality Patterns

## Directory.Build.props (solution root)

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <NuGetAudit>true</NuGetAudit>
    <NuGetAuditLevel>low</NuGetAuditLevel>
    <NuGetAuditMode>all</NuGetAuditMode>
  </PropertyGroup>

  <ItemGroup>
    <!-- Roslyn Analyzers -->
    <PackageReference Include="Roslynator.Analyzers" Version="4.12.0">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="Meziantou.Analyzer" Version="2.0.180">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

If your CI runs SonarQube, `SonarAnalyzer.CSharp` runs the same rules in the local build. Check its
current licence before adding it: from 10.x it ships under the Sonar Source-Available License, which
limits some uses. Roslynator, Meziantou and the .NET analyzers cover most of the same ground.

## Coverlet (code coverage)

```xml
<!-- Directory.Build.props for test projects -->
<ItemGroup Condition="$(MSBuildProjectName.EndsWith('.Tests'))">
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="coverlet.msbuild" Version="6.0.4" />
</ItemGroup>
```

### Run with thresholds
```bash
# Collect coverage
dotnet test --collect:"XPlat Code Coverage" \
  --results-directory ./coverage

# With thresholds (fail build if below)
dotnet test /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=./coverage/ \
  /p:Threshold=80 \
  /p:ThresholdType=line \
  /p:ThresholdStat=total

# Generate HTML report
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator \
  -reports:./coverage/**/coverage.cobertura.xml \
  -targetdir:./coverage/report \
  -reporttypes:Html
```

### Exclude from coverage
```csharp
[ExcludeFromCodeCoverage]  // for generated code, DTOs, or infrastructure wiring
public static class ServiceRegistration { }

// In .csproj: exclude specific namespaces
// /p:ExcludeByFile="**/Migrations/**"
// /p:Exclude="[*]*.Migrations.*"
```

## BenchmarkDotNet

```csharp
// Benchmark project setup
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100)]  // .NET 10
public class OrderQueryBenchmarks
{
    private AppDbContext _context = null!;
    private NpgsqlConnection _connection = null!;
    private Guid _customerId;

    [GlobalSetup]
    public async Task Setup()
    {
        // Setup real database connection for benchmarks
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=benchmarks;...")
            .Options;
        _context = new AppDbContext(options);
        _connection = new NpgsqlConnection("Host=localhost;Database=benchmarks;...");
        await _connection.OpenAsync();
        _customerId = Guid.Parse("...");  // known test data
    }

    [Benchmark(Baseline = true)]
    public async Task<List<Order>> EfCore_WithTracking()
    {
        return await _context.Orders
            .Where(o => o.CustomerId == _customerId)
            .Take(20)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<Order>> EfCore_NoTracking()
    {
        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == _customerId)
            .Take(20)
            .ToListAsync();
    }

    [Benchmark]
    public async Task<List<OrderDto>> Dapper_RawSql()
    {
        return (await _connection.QueryAsync<OrderDto>(
            "SELECT id, total, status FROM orders WHERE customer_id = @Id LIMIT 20",
            new { Id = _customerId })).AsList();
    }

    [Benchmark]
    public async Task<Order?> EfCore_CompiledQuery()
    {
        return await OrderQueries.GetById(_context, _customerId, CancellationToken.None);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _context.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

// Run: dotnet run -c Release --project Benchmarks/
```

## .editorconfig

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
# Naming rules
dotnet_naming_rule.private_fields.symbols = private_fields
dotnet_naming_rule.private_fields.style = camel_case_underscore
dotnet_naming_rule.private_fields.severity = error

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.camel_case_underscore.capitalization = camel_case
dotnet_naming_style.camel_case_underscore.required_prefix = _

# Code style
csharp_style_namespace_declarations = file_scoped:error
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_prefer_primary_constructors = true:suggestion
csharp_prefer_simple_using_statement = true:suggestion
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_prefer_switch_expression = true:suggestion
csharp_style_prefer_pattern_matching = true:suggestion

# Formatting
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true

# Analyzer severities
dotnet_diagnostic.CA1062.severity = none  # null check on public params (nullable handles this)
dotnet_diagnostic.CA1822.severity = suggestion  # mark as static
dotnet_diagnostic.CA2007.severity = none  # ConfigureAwait in ASP.NET Core
dotnet_diagnostic.CS8618.severity = error  # non-nullable not initialized
```

## Quality Gate Script (CI)

```bash
#!/bin/bash
set -e

echo "=== Build ==="
dotnet build --no-restore -warnaserror

echo "=== Unit Tests with Coverage ==="
dotnet test --no-build \
  --filter "Category!=Integration&Category!=Architecture" \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:Threshold=80 \
  /p:ThresholdType=line

echo "=== Architecture Tests ==="
dotnet test --no-build --filter "Category=Architecture"

echo "=== Integration Tests ==="
dotnet test --no-build --filter "Category=Integration"

echo "=== NuGet Vulnerability Check ==="
dotnet list package --vulnerable --include-transitive | grep -i "has the following vulnerable" && exit 1 || true

echo "=== All quality gates passed ==="
```
