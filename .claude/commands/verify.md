---
name: verify
description: Full verification loop — build, unit tests with coverage, architecture tests, integration tests, and vulnerability check. Run before creating a PR.
allowed-tools: Read, Bash, Grep, Glob
---

## Full Verification Loop

Execute each step sequentially. Stop on first failure.

### Step 1: Build
```bash
dotnet build --no-restore -warnaserror
```

### Step 2: Unit Tests with Coverage
```bash
dotnet test --no-build \
  --filter "Category!=Integration&Category!=Architecture" \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:Threshold=80 \
  /p:ThresholdType=line
```

### Step 3: Architecture Tests
```bash
dotnet test --no-build --filter "Category=Architecture"
```

### Step 4: Integration Tests
```bash
dotnet test --no-build --filter "Category=Integration"
```

### Step 5: Code Format Check
```bash
dotnet format --verify-no-changes
```

### Step 6: NuGet Vulnerability Check
```bash
dotnet list package --vulnerable --include-transitive
```

### On Success
Report: "All verification gates passed. Ready for PR."

### On Failure
Report which step failed, the specific error, and the suggested fix.
Reference the appropriate skill for remediation guidance.
