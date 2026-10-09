# Testing Requirements

## TDD Mandate
Every feature and bug fix MUST follow the Red-Green-Refactor cycle:
1. RED — Write failing tests that define expected behavior
2. GREEN — Write minimum code to make tests pass
3. REFACTOR — Clean up while keeping tests green

No implementation code without a failing test first. No exceptions.

## Stack
- **xUnit 2.x** — test framework (not NUnit, not MSTest)
- **FluentAssertions 7.x ONLY — never 8.0 or later** — readable assertions:
  `result.Should().Be(expected)`. **7.x is the last Apache-2.0 (fully open-source) version.** From 8.0
  the library moved to the Xceed Community License, which requires a **paid license for commercial
  use**. A silent `dotnet outdated`/Dependabot bump to 8.x puts a commercial project in breach, so
  **pin the range explicitly** in every test project:
  ```xml
  <PackageReference Include="FluentAssertions" Version="[7.0.0,8.0.0)" />
  ```
  The bracketed range allows 7.x patch/minor fixes but blocks 8.0+. (An exact pin `[7.0.0]` also works
  but forfeits 7.x fixes.) Prefer this over an unpinned `Version="7.0.0"`, which NuGet may float upward.
  If a project must move off FluentAssertions entirely, the license-safe options are the community fork
  **AwesomeAssertions** (original Apache license) or **Shouldly**. Never upgrade to 8+ in a
  commercial project without a purchased license.
- **Moq 4.x or NSubstitute** — mocking. Use the one the codebase already uses, and never mix the two
  in one test project. With no existing choice, Moq is the default here, unless your team's rules say
  otherwise.
- **AutoFixture** — test data generation (`AutoFixture.AutoMoq` or `AutoFixture.AutoNSubstitute`)
- **Testcontainers** — real databases in integration tests (PostgreSQL, SQL Server, Redis, MongoDB, Kafka)
- **Respawn** — database reset between integration tests (`TablesToIgnore: ["__EFMigrationsHistory"]`)
- **WebApplicationFactory\<Program\>** — API integration tests (works with top-level statements in .NET 10)
- **NetArchTest** — architecture validation (layer dependencies, naming conventions)
- **Coverlet** — code coverage collection
- **BenchmarkDotNet** — performance benchmarks for critical paths

## Coverage Thresholds
- **80% line coverage** minimum — enforced in CI via Coverlet
- **70% branch coverage** minimum
- Zero test failures on the main branch — no exceptions, no `[Skip]` without an issue number

## Naming Convention
`MethodName_Scenario_ExpectedBehavior`

Examples:
- `GetOrderById_OrderExists_ReturnsOrder`
- `CreateUser_DuplicateEmail_ThrowsConflictException`
- `ProcessPayment_InsufficientFunds_ReturnsFailureResult`

## Patterns
- Arrange-Act-Assert structure. Clear separation between phases.
- One assertion concept per test (multiple `Should()` calls OK if testing same concept).
- `[Fact]` for single cases. `[Theory]` with `[InlineData]` or `[MemberData]` for parameterized.
- No test-to-test dependencies. Every test must run in isolation and in any order.
- Never use in-memory database providers (EF InMemory) — use Testcontainers with real engines.
- Integration tests use `IAsyncLifetime` for setup/teardown.
- Test doubles (Moq or NSubstitute) for unit tests. Real dependencies (via Testcontainers) for integration tests.
