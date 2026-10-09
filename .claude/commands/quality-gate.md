---
name: quality-gate
description: Run all quality checks — code coverage thresholds, Roslyn analyzer warnings, architecture tests, and format verification. Use as a pre-merge gate.
allowed-tools: Read, Bash, Grep, Glob
---

## Quality Gate

All checks must pass. Reference `skills/code-quality/` for configuration details.

### 1. Roslyn Analyzers (zero warnings)
```bash
dotnet build -warnaserror --no-restore
```
Analyzers enforced: Roslynator, Meziantou and the .NET analyzers (configured in `Directory.Build.props`).

### 2. Code Coverage (80% line minimum)
```bash
dotnet test --no-build \
  --filter "Category!=Integration" \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:Threshold=80 \
  /p:ThresholdType=line \
  /p:ThresholdStat=total
```

### 3. Architecture Tests
```bash
dotnet test --no-build --filter "Category=Architecture"
```

### 4. Format Verification
```bash
dotnet format --verify-no-changes
```

### 5. Zero Vulns (no known-vulnerable packages — hard fail)
```bash
dotnet restore --force-evaluate
# NuGetAudit (NuGetAuditMode=all) is enabled in Directory.Build.props with NU1901-NU1904 as errors,
# so the build FAILS on any vulnerable package — direct or transitive, any severity.
dotnet list package --vulnerable --include-transitive   # explicit confirmation
```
Any known vulnerability (direct or transitive, any severity) fails this gate — it's zero known vulns,
not zero-critical. Remediate per `skills/zero-vulns/` (upgrade, or pin the fixed transitive version).

### Report
Output pass/fail status for each gate. If any gate fails, report the specific violations and reference the appropriate rule or skill for fixes.
