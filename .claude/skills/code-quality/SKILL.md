---
name: code-quality
description: "Code quality gates for .NET: Roslyn analyzers (Roslynator/Meziantou/.NET), warnings-as-errors, format verification, architecture tests."
version: 1.2.0
---

# Code Quality Patterns

## Directory.Build.props (solution root)

<!-- sample: tests/SkillSamples.Tests/CodeQuality/sample/Directory.Build.props -->
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

Measure before optimising a hot path, with `[MemoryDiagnoser]`, against a real database, in Release.
The `/benchmark` command sets it up.

## .editorconfig

<!-- sample: tests/SkillSamples.Tests/CodeQuality/sample/.editorconfig -->
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
dotnet_diagnostic.IDE1006.severity = error  # the build only enforces naming rules through this line

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
dotnet_diagnostic.MA0004.severity = none  # Meziantou's ConfigureAwait rule: the same, so off too
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

## Silent failures: a failure that passes for success

The bug a patient sees as a wrong answer, not an error: an insurer outage shown as "0% covered", a lab
value that isn't on file read as 0, a claim marked sent after its send failed. No analyzer flags these, so
a review looks for them:

```bash
python ~/.claude/skills/code-quality/scripts/silent_failures.py <code folder> [--diff change.diff]
```

It lists each candidate with its kind: `swallowed` (an empty catch), `fallback` (a catch that returns a
success-looking value), `logged and ignored`, `skipped` (a catch that `continue`s), `default for missing`
(`?? 0` after a lookup), `not awaited` (`_ = SomethingAsync(...)`) and `parse ignored`. It leaves out a
catch that rethrows, wraps, returns an error result (`Problem`, a 4xx or 5xx) or catches a cancellation.

Each candidate gets a verdict, with the reason:
- **Silent failure:** the caller, the user or the next step can't tell it failed. The fix makes the failure
  visible where it matters: let it throw, return an error result or a `Result` failure, mark the item
  failed instead of done, or `await` it. Logging alone is not a fix: nobody reads the log at the moment
  the wrong answer is shown.
- **Fine:** the fallback is the design (a cache miss reads the database, a best-effort metric), and the
  code or a comment says so.

