---
name: tdd
description: Execute TDD workflow — write failing tests first, implement minimally, refactor. Use for any new feature or bug fix implementation.
allowed-tools: Read, Write, Edit, Bash, Grep, Glob
---

Delegate to the **tdd-guide** agent.

Follow the Red-Green-Refactor cycle strictly:

1. **RED**: Write 3-5 failing tests covering happy path, edge cases, and error conditions
   - Reference `skills/testing-tdd/` for xUnit + FluentAssertions 7.x + mocking patterns
   - Run `dotnet test --filter "FullyQualifiedName~<TestClass>"` — tests MUST FAIL
2. **GREEN**: Implement minimum code to make all tests pass
   - No optimization, no future-proofing — just make tests green
   - Run tests again — ALL MUST PASS
3. **REFACTOR**: Clean up while keeping tests green
   - Reference `skills/ddd-patterns/` for domain modeling
   - Reference `skills/dotnet-core/` for modern C# idioms
   - Run `dotnet format` after refactoring
4. **INTEGRATE**: Add integration tests if the feature touches persistence
   - Reference `skills/testing-integration/` for Testcontainers patterns
   - Run `dotnet test --filter "Category=Architecture"` to verify layer rules
