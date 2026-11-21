---
name: tdd
description: "Work test-first: write the failing tests, implement the minimum, refactor."
allowed-tools: Read, Write, Edit, Bash, Grep, Glob, Agent
---

Delegate to **qa-engineer** first, for a story or a plain request alike: it writes `story.md` (the
criteria, inferred and marked when there's no story, and every edge case as an `EC` line) and the failing
tests (`test-design`). When the change carries a high risk (a risk of 6 or more: a patient, another person's
data, consent, money), start **adversary** in the same message, so the two run at once: it writes
`abuse.md` and the failing abuse tests, never `story.md`. On other changes qa-engineer asks its four
questions itself (`test-design`, section 3c): the model refuses ordinary misuse unprompted, so the extra
agent pays only where a slip would hurt someone (`tests/evals/fix-abuse*`). Then delegate to the **tdd-guide** agent, which makes them
all pass without editing them. Skip qa-engineer only for a change with no behaviour (a rename, a comment,
a package bump).

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
