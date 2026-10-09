# C# Standards

## Modern C# Features (adopt by version)

### C# 12+ (.NET 8+)
- Primary constructors for DI injection in services: `public class OrderService(IOrderRepository repo, ILogger<OrderService> logger)`
- Collection expressions: `int[] ids = [1, 2, 3];` over `new int[] { 1, 2, 3 }`
- Required members: `public required string Name { get; init; }`
- Raw string literals for multi-line SQL, JSON, XML
- `nameof()` in attributes

### C# 13+ (.NET 9+)
- Partial properties for source-generated code
- `params` with `ReadOnlySpan<T>` and collections

### C# 14+ (.NET 10+)
- `field` keyword in property accessors — no explicit backing field needed
- Extension blocks: `extension(string s) { public bool IsEmail => s.Contains("@"); }`
- Null-conditional assignment: `obj?.Property ??= defaultValue;`
- `nameof` on unbound generics: `nameof(List<>)` → `"List"`
- Partial constructors and events
- Lambda parameter modifiers: `(ref int x) => x++`

When targeting .NET 8, limit to C# 12 features. When targeting .NET 10, use C# 14 fully.

## Naming Conventions
- **PascalCase**: classes, methods, properties, public fields, constants, enums, events
- **camelCase**: local variables, parameters, lambda parameters
- **_camelCase**: private fields (underscore prefix)
- **I prefix**: interfaces (`IOrderRepository`, `INotificationService`)
- **Async suffix**: async methods (`GetOrderAsync`, `PublishEventAsync`)
- **Base suffix**: abstract base classes (`EntityBase`, `AggregateRootBase`)
- No Hungarian notation. No abbreviations except universally known ones (Id, Url, Http, Dto).

## DI Lifetime Rules
- **Scoped**: DbContext, repositories, unit of work, current user/tenant services
- **Singleton**: HttpClient factories, ConnectionMultiplexer (Redis), MongoClient, IMemoryCache, configuration objects
- **Transient**: Stateless services, validators, mappers
- **NEVER** inject Scoped into Singleton — causes captive dependency. Use `IServiceScopeFactory` instead.

## Critical Rules (NEVER VIOLATE)
- NEVER call `.Result` or `.Wait()` on tasks — async all the way down
- NEVER instantiate `HttpClient` directly — use `IHttpClientFactory` or typed clients
- NEVER use `DateTime.Now` or `DateTime.UtcNow` — inject `TimeProvider` for testability
- NEVER use `BinaryFormatter` — use `System.Text.Json` (insecure deserialization)
- NEVER use `Thread.Sleep()` — use `await Task.Delay()` with `TimeProvider`
- ALWAYS enable nullable reference types — treat CS8600/CS8602/CS8603 as errors
- ALWAYS use `CultureInfo.InvariantCulture` for parsing/formatting non-user-facing data
- NEVER let `ar-SA` become the formatting culture: its default calendar is Hijri (`2026` parses as a Hijri
  year and fails) and its numbers use Arabic separators. A language header sets the UI culture only —
  see `skills/localization/`.
- ALWAYS seal classes unless explicitly designed for inheritance
- Database primary keys are `int` identity (`bigint` for very large tables) unless your team's rules say
  otherwise — see `rules/efcore-rules.md`. GUIDs for IDs created outside the database; then
  `Guid.CreateVersion7()`, never random v4.

## Banned Patterns
- Service Locator — use constructor injection exclusively
- Lazy loading in EF Core — use explicit `.Include()` or projections
- God controllers — max 5-7 endpoints per controller or endpoint group
- Anemic domain models — entities must have behavior, not just properties
- Throwing exceptions for control flow — use Result/OneOf pattern for expected failures
- `catch (Exception) { }` — never swallow exceptions silently
