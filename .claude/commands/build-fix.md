---
name: build-fix
description: Diagnose and fix build failures, compiler errors, NuGet conflicts, test failures, and runtime exceptions. Use when the build is broken.
allowed-tools: Read, Write, Edit, Bash, Grep, Glob
---

Delegate to the **build-error-resolver** agent.

1. Run `dotnet build` and capture the full error output
2. Categorize the error: compiler (CS-prefixed), NuGet, runtime, test, Docker, CI
3. Find the root cause — check `git diff` for recent changes
4. Apply the minimal fix
5. Run `dotnet build` again to confirm
6. Run `dotnet test` to ensure no regressions
7. If the error was preventable, suggest an analyzer rule to catch it in the future
