# Testing Requirements

## TDD
Every feature and bug fix goes red, green, refactor: a failing test first, the least code that passes
it, then clean up with the tests green. No implementation code without a failing test first.

## Stack
- **xUnit 2.x** (not NUnit or MSTest).
- **FluentAssertions 7.x only, pinned `Version="[7.0.0,8.0.0)"`**: 8.0 and later need a paid licence for
  commercial use, and an unpinned version can float up. Why and the alternatives: `testing-tdd`.
- **Moq 4.x or NSubstitute**: the one the codebase uses, never both in one test project; Moq when there's
  no choice yet, unless your team's rules say otherwise. **AutoFixture** for test data.
- **Testcontainers** for real databases and brokers, **Respawn** between tests
  (`TablesToIgnore: ["__EFMigrationsHistory"]`), **WebApplicationFactory\<Program\>** for API tests.
- **NetArchTest** for architecture, **Coverlet** for coverage, **BenchmarkDotNet** for hot paths.

## Coverage
- At least 80% line and 70% branch coverage, enforced in CI.
- No failing test on the main branch, and no `[Skip]` without an issue number.

## Naming
`MethodName_Scenario_ExpectedBehavior`, for example `GetOrderById_OrderExists_ReturnsOrder`.

## Patterns
- Arrange, act, assert, clearly apart; one assertion concept per test.
- `[Fact]` for one case, `[Theory]` with `[InlineData]` or `[MemberData]` for several.
- Every test runs alone and in any order.
- Never the EF InMemory provider: Testcontainers with the real engine.
- Integration tests use `IAsyncLifetime` for setup and teardown.
- Test doubles in unit tests; real dependencies, through Testcontainers, in integration tests.
