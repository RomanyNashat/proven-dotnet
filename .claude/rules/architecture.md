# Architecture Standards

## Clean Architecture layers

```
API (endpoints or controllers)      → depends on Application
Application (commands, queries)     → depends on Domain
Infrastructure (EF Core, messaging) → depends on Domain + Application
Domain (entities, value objects)    → depends on nothing
```

What goes in each layer is in `ddd-patterns` ("The layers").

## Dependency rules (enforced by NetArchTest, `testing-architecture`)
- Domain does not reference Application, Infrastructure or API.
- Application does not reference Infrastructure or API.
- Infrastructure does not reference API.
- No circular dependencies between projects.
- Repository interfaces live in Domain, implementations in Infrastructure.
- Endpoints and controllers never touch the DbContext: they go through the Application layer.

## Services
- One bounded context per service, and no database shared between services. Others learn of its
  changes through events.

## Configuration
- Strongly typed options, validated at start-up with `ValidateOnStart()` (`dotnet-core`).
- Feature flags for progressive rollouts; never `#if DEBUG` for behaviour.

## Errors
- A global exception handler returns ProblemDetails, with no internal detail in production.
- Expected failures are results (OneOf, ErrorOr); exceptions are for the unexpected, and a broken
  business rule is a domain exception (`OrderCannotBeCancelledException`).
- Log exceptions structured, with a correlation id.
