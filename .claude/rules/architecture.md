# Architecture Standards

## Clean Architecture Layers

```
API (Controllers/Endpoints) → depends on Application
Application (Commands/Queries/DTOs) → depends on Domain
Infrastructure (EF Core/Kafka/Redis) → depends on Domain + Application
Domain (Entities/ValueObjects/Events) → depends on NOTHING
```

### Domain Layer
- Zero external dependencies. No NuGet packages except pure abstractions.
- Contains: Entities, Aggregate Roots, Value Objects, Domain Events, Repository interfaces, Domain Services.
- Entities encapsulate behavior — state changes through methods, not public setters.
- Value Objects are C# records: `public sealed record Money(decimal Amount, string Currency);`
- Domain Events are raised by aggregates, collected internally, dispatched after persistence.

### Application Layer
- Contains: Commands, Queries, DTOs, Validators, Pipeline Behaviors, Application Services.
- CQRS split: `ICommand<TResult>` and `IQuery<TResult>` marker interfaces.
- One handler per command/query. Handler classes are sealed.
- FluentValidation validators per command. Validation runs in pipeline behavior before handler.
- No direct database access — uses repository interfaces defined in Domain.

### Infrastructure Layer
- Contains: EF Core DbContext, Repository implementations, Kafka producers/consumers, Redis clients, external API clients.
- Implements interfaces from Domain and Application layers.
- EF Core configurations in `EntityTypeConfiguration<T>` classes — not in DbContext.OnModelCreating.
- One migration per logical change. Migrations must be idempotent and backward-compatible.

### API Layer
- Contains: Endpoints (Minimal APIs) or Controllers, middleware, filters, mappers.
- Thin layer — maps HTTP to commands/queries, delegates to Application layer.
- No business logic in controllers/endpoints. Max 5-7 endpoints per group.
- Returns `TypedResults` (Minimal APIs) or `IActionResult` (controllers) with proper status codes.

## Dependency Rules (enforced by NetArchTest)
- Domain MUST NOT reference Application, Infrastructure, or API.
- Application MUST NOT reference Infrastructure or API.
- Infrastructure MUST NOT reference API.
- No circular dependencies between projects.
- Repositories are interfaces in Domain, implementations in Infrastructure.
- Controllers/endpoints MUST NOT directly reference DbContext — go through Application layer.

## Microservice Boundaries
- One bounded context per microservice. No shared databases between services.
- Communication: synchronous (gRPC for internal, REST for external) + asynchronous (Kafka events).
- Each service owns its data. Other services get eventual consistency via domain events.
- API Gateway (YARP) for external traffic routing, auth offloading, rate limiting.
- Service discovery via .NET Aspire or Kubernetes DNS.

## Configuration
- `appsettings.json` for defaults. `appsettings.{Environment}.json` for overrides.
- Strongly typed options: `services.Configure<KafkaOptions>(config.GetSection("Kafka"))` with validation.
- Validate configuration at startup: `ValidateOnStart()` — fail fast, not at runtime.
- Feature flags for progressive rollouts. Never use #if DEBUG for behavior changes.

## Error Handling
- Global exception handler middleware — catches unhandled exceptions, returns ProblemDetails.
- Domain exceptions for business rule violations: `OrderCannotBeCancelledException`.
- Use Result pattern (OneOf/ErrorOr) for expected failures — reserve exceptions for unexpected states.
- Never expose internal details in production error responses.
- Structured logging of exceptions with correlation IDs for distributed tracing.
