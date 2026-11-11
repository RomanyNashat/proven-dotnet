# C# Standards

## Language version
- .NET 8 means C# 12 features only; .NET 10 means C# 14 in full (`field`, extension blocks,
  null-conditional assignment). Modern C# and examples: `dotnet-core`.

## Naming
- PascalCase for types, members, constants, enums and events; camelCase for locals and parameters;
  `_camelCase` for private fields.
- `I` prefix on interfaces, `Async` suffix on async methods, `Base` suffix on abstract base classes.
- No Hungarian notation, no abbreviations but the universal ones (Id, Url, Http, Dto).

## DI lifetimes
- Scoped: DbContext, repositories, unit of work, current user or tenant.
- Singleton: HTTP client factories, Redis `ConnectionMultiplexer`, `MongoClient`, `IMemoryCache`, options.
- Transient: stateless services, validators, mappers.
- **Never** a scoped service inside a singleton (a captive dependency): use `IServiceScopeFactory`.

## Critical rules (never violate)
- NEVER call `.Result` or `.Wait()` on tasks: async all the way down.
- NEVER instantiate `HttpClient` directly: `IHttpClientFactory` or typed clients.
- NEVER use `DateTime.Now` or `DateTime.UtcNow`: inject `TimeProvider`.
- NEVER use `BinaryFormatter`: `System.Text.Json`.
- NEVER use `Thread.Sleep()`: `await Task.Delay()` with `TimeProvider`.
- ALWAYS enable nullable reference types, with CS8600, CS8602 and CS8603 as errors.
- ALWAYS use `CultureInfo.InvariantCulture` for parsing and formatting data no person reads.
- NEVER let `ar-SA` become the formatting culture: its calendar is Hijri or Gregorian depending on the
  machine's ICU, and its numbers can use Arabic separators. A language header sets the UI culture only;
  a date a person reads names its calendar (`localization`).
- ALWAYS seal classes unless they are designed for inheritance.
- Primary keys are `int` identity (`bigint` for very large tables) unless your team's rules say
  otherwise (`rules/efcore-rules.md`). A GUID only for an id made outside the database, and then
  `Guid.CreateVersion7()`, never a random v4.

## Banned patterns
- Service Locator: constructor injection only.
- Lazy loading in EF Core: explicit `.Include()` or projections.
- God controllers: at most 5 to 7 endpoints per controller or endpoint group.
- Anemic domain models: entities have behaviour, not just properties.
- Exceptions for control flow: a Result or OneOf for expected failures.
- `catch (Exception) { }`: never swallow an exception.
