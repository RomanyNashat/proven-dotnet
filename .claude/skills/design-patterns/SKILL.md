---
name: design-patterns
description: Design patterns in modern C#/.NET — GoF and enterprise — when each fits, when it's over-engineering, what .NET already gives you, and the misuses to flag. Used by /patterns (explain and suggest), pattern-analyst, architect, code-reviewer, refactor-cleaner.
version: 1.0.0
---

# Design Patterns for .NET

A pattern is a name for a shape of code that solves a recurring problem. The name is the useful part:
"this is a decorator" tells a reader in three words what the class does and what it doesn't. The risk is
the opposite direction, starting from the pattern and looking for a place to put it.

This skill has two jobs, and `/patterns` has a mode for each:
- **Explain:** find the patterns in existing code, say how each one works *here*, and judge the fit.
- **Suggest:** given a problem, say which pattern fits (or that none is needed) and sketch it.

Outbox, CQRS, DDD building blocks, resilience and caching have their own skills. This one links to them
instead of repeating them.

## 1. Judging a pattern

A pattern earns its place when it removes a cost the code **has today**:
- a `switch` on the same type in several places that grows every sprint;
- the same cross-cutting concern (caching, logging, retries, auditing) copied into many classes;
- an external system's model leaking into the domain;
- a lifecycle enforced by scattered `bool` flags.

It's over-engineering when:
- an interface has one implementation and no I/O behind it (a test seam at an I/O boundary is fine);
- a factory only calls `new`, or a builder builds a type with three fields;
- an abstract base class has one subclass;
- the pattern is in the class name (`NotificationStrategyFactoryProvider`) because the pattern was the
  goal;
- following one request means opening five files that each forward a call.

"We might need it later" is not a cost the code has today. Adding a seam later, when the second case
arrives, is a small refactor. Carrying an unused one costs every reader.

### Verdicts

`/patterns` and the reviewers use these five words, so a finding reads the same everywhere:

| Verdict | Meaning |
|---|---|
| **Fits** | The pattern removes a real cost here. Keep it. |
| **Over-engineered** | The pattern is correct but the problem doesn't need it. Name the simpler shape. |
| **Misused** | The pattern is there but broken (see §5). Name the defect and the fix. |
| **Missing** | The code pays a cost a pattern would remove. Name the pattern and the evidence. |
| **Built into .NET** | Hand-rolled something the framework already does (see §2). Name the built-in. |

Every verdict cites the code it's based on (file and symbol), not a general principle.

## 2. What .NET already gives you

Check this table before writing a pattern by hand.

| You want | .NET already has |
|---|---|
| Singleton | `AddSingleton`. Never a static `Instance` property. |
| Factory | DI with a factory delegate; `IHttpClientFactory`; `IDbContextFactory<T>`; `ActivatorUtilities` |
| Strategy chosen at compile time | Keyed services: `[FromKeyedServices(key)]` (.NET 8+) |
| Decorator | A DI registration that wraps the real type (§3.2); `DelegatingHandler` for `HttpClient` |
| Chain of Responsibility | ASP.NET Core middleware; endpoint filters; `DelegatingHandler` |
| Observer, in process | C# events; `IObservable<T>`; domain events (`ddd-patterns`); `Channel<T>` (`worker-patterns`) |
| Observer, across services | Kafka (`kafka-patterns`) with an outbox (`outbox`) |
| Command queued for later | `Channel<T>` + `BackgroundService`; Hangfire or Quartz for persisted jobs |
| Iterator | `IEnumerable<T>` with `yield return`; `IAsyncEnumerable<T>` |
| Builder for configuration | `WebApplicationBuilder`; the options pattern with `ValidateOnStart()` |
| Prototype, Memento | Records and `with` |
| Object pool | `ObjectPool<T>`, `ArrayPool<T>.Shared` |
| Retry, circuit breaker, timeout, bulkhead | Polly v8 pipelines (`polly-resilience`) |
| Unit of Work | `DbContext` and `SaveChangesAsync` already are one (in Clean Architecture, a thin `IUnitOfWork` port is still how Application commits: §4) |
| Proxy for lazy loading | Don't. Lazy loading is banned (`rules/csharp-standards.md`). |

## 3. Patterns with tested samples

The code in this section compiles and its tests run in CI (`tests/SkillSamples.Tests/Patterns`).

### 3.1 Strategy: pick an algorithm at runtime

**Problem.** Several ways to do one job (send by SMS, email or push; price by insurer; export as CSV or
PDF), chosen per call.
**Reach for it when** the same `switch` appears in more than one place, or each branch has its own
dependencies.
**Skip it when** there is one `switch` in one place with a line or two per branch. The `switch` is the
simpler strategy.
**Misuse:** a strategy per branch that differs only by a constant (that's data, use a dictionary); a
"strategy factory" that resolves from `IServiceProvider` (that's a service locator).

Runtime choice: inject every implementation and build a map once.

<!-- sample: tests/SkillSamples.Tests/Patterns/Strategy.cs -->
```csharp
public enum NotificationChannel { Sms, Email, Push }

public interface INotificationSender
{
    NotificationChannel Channel { get; }
    Task SendAsync(string to, string text, CancellationToken ct);
}

// The router picks a strategy at runtime from the channel the caller asks for. Building the map in the
// constructor means a second sender for the same channel fails at startup, not on the first message.
public sealed class NotificationRouter
{
    private readonly IReadOnlyDictionary<NotificationChannel, INotificationSender> _senders;

    public NotificationRouter(IEnumerable<INotificationSender> senders) =>
        _senders = senders.ToDictionary(s => s.Channel);

    public Task SendAsync(NotificationChannel channel, string to, string text, CancellationToken ct) =>
        _senders.TryGetValue(channel, out var sender)
            ? sender.SendAsync(to, text, ct)
            : throw new NotSupportedException($"No sender registered for {channel}.");
}

// When the choice is fixed at compile time, a keyed service is enough: no router, no switch.
//   services.AddKeyedSingleton<INotificationSender, SmsSender>(NotificationChannel.Sms);
public sealed class OtpService([FromKeyedServices(NotificationChannel.Sms)] INotificationSender sms)
{
    public Task SendCodeAsync(string mobile, string code, CancellationToken ct) =>
        sms.SendAsync(mobile, $"Your code is {code}", ct);
}
```

### 3.2 Decorator: add behaviour around a type without changing it

**Problem.** Caching, logging, metrics, retries or authorization around an existing service, without
editing it or its callers.
**Reach for it when** the concern is the same for every method of the interface, and you'd otherwise
copy it into the class.
**Skip it when** only one method needs it. Put it in the method.
**Misuse:** a decorator that changes what the call means (swallows exceptions, returns defaults on
failure); a stack of decorators whose order matters but isn't written down; caching per pod with
`IMemoryCache` in a multi-pod service where staleness matters (use HybridCache with a Redis backplane,
see `caching`; the decorator shape stays the same).

<!-- sample: tests/SkillSamples.Tests/Patterns/Decorator.cs -->
```csharp
public sealed record ClinicInfo(int Id, string NameEn, string NameAr);

public interface IClinicDirectory
{
    Task<ClinicInfo?> FindAsync(int clinicId, CancellationToken ct);
}

// Adds caching without touching the real directory or its callers. A miss (null) is cached too, so an
// unknown id doesn't reach the database on every request.
public sealed class CachedClinicDirectory(IClinicDirectory inner, IMemoryCache cache) : IClinicDirectory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    public Task<ClinicInfo?> FindAsync(int clinicId, CancellationToken ct) =>
        cache.GetOrCreateAsync(("clinic", clinicId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return inner.FindAsync(clinicId, ct);
        });
}

public static class ClinicDirectoryRegistration
{
    // The real directory is registered as itself; the interface resolves to the decorator wrapping it.
    // No Scrutor needed for one decorator.
    public static IServiceCollection AddClinicDirectory<TDirectory>(this IServiceCollection services)
        where TDirectory : class, IClinicDirectory
    {
        services.AddMemoryCache();
        services.AddScoped<TDirectory>();
        services.AddScoped<IClinicDirectory>(sp => new CachedClinicDirectory(
            sp.GetRequiredService<TDirectory>(), sp.GetRequiredService<IMemoryCache>()));
        return services;
    }
}
```

For more than two or three decorators, or open generics, see the registration helper in
`cqrs-eventsourcing` before adding Scrutor.

### 3.3 State: a lifecycle with rules about what can follow what

**Problem.** An entity moves through states (booked, checked in, completed, cancelled), and only some
moves are allowed.
**Reach for it when** the rules live in scattered `bool` flags (`IsCancelled`, `IsCheckedIn`), or the
same "can this happen now?" check is repeated in several handlers.
**Start with a transition table** in a `switch` expression: one place, readable, easy to test. **Move to
a class per state** only when each state has substantial behaviour of its own, not just a different
next step. Most healthcare workflows never need that step.
**Misuse:** state classes that each hold one line; a public setter on the status next to the state
machine, which lets callers skip it.

<!-- sample: tests/SkillSamples.Tests/Patterns/AppointmentState.cs -->
```csharp
public enum AppointmentStatus { Booked, CheckedIn, Completed, Cancelled, NoShow }

public enum AppointmentTrigger { CheckIn, Complete, Cancel, MissWindow }

public sealed class Appointment
{
    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Booked;

    // The whole state machine in one place. Anything not listed is not allowed.
    public bool TryApply(AppointmentTrigger trigger)
    {
        AppointmentStatus? next = (Status, trigger) switch
        {
            (AppointmentStatus.Booked, AppointmentTrigger.CheckIn) => AppointmentStatus.CheckedIn,
            (AppointmentStatus.Booked, AppointmentTrigger.Cancel) => AppointmentStatus.Cancelled,
            (AppointmentStatus.Booked, AppointmentTrigger.MissWindow) => AppointmentStatus.NoShow,
            (AppointmentStatus.CheckedIn, AppointmentTrigger.Complete) => AppointmentStatus.Completed,
            _ => null,
        };
        if (next is null)
        {
            return false;
        }

        Status = next.Value;
        return true;
    }
}
```

### 3.4 Specification: named, composable query rules

**Problem.** The same filter ("active", "for this clinic", "upcoming") is written in several queries,
and combinations differ per screen.
**Reach for it when** a rule is repeated in more than one query, or users build filters by combining
rules.
**Skip it when** a query is used once. An inline `Where` is clearer.
**The trap:** combining expressions with `Expression.Invoke` compiles and works on lists, then fails in
EF Core because an invocation can't be translated to SQL. Rebind the parameter instead, as below.
**Misuse:** specification classes with a `Compile()`d predicate passed to `Where` on a `DbSet`. That
runs the filter in memory after loading the table.

<!-- sample: tests/SkillSamples.Tests/Patterns/Specification.cs -->
```csharp
public sealed record AppointmentRow(int Id, int ClinicId, DateTimeOffset StartsAt, AppointmentStatus Status);

// Named, reusable query rules. They are expressions, so EF Core turns them into SQL.
public static class AppointmentSpecs
{
    public static readonly Expression<Func<AppointmentRow, bool>> NotCancelled =
        a => a.Status != AppointmentStatus.Cancelled;

    public static Expression<Func<AppointmentRow, bool>> ForClinic(int clinicId) => a => a.ClinicId == clinicId;

    public static Expression<Func<AppointmentRow, bool>> StartingFrom(DateTimeOffset from) => a => a.StartsAt >= from;
}

public static class ExpressionComposition
{
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.AndAlso);

    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> left, Expression<Func<T, bool>> right) =>
        Combine(left, right, Expression.OrElse);

    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> rule) =>
        Expression.Lambda<Func<T, bool>>(Expression.Not(rule.Body), rule.Parameters);

    // Rebind the right side to the left side's parameter. Expression.Invoke would also compile, but EF
    // Core can't translate an invocation to SQL, so the query fails at runtime.
    private static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right, Func<Expression, Expression, BinaryExpression> op)
    {
        var parameter = left.Parameters[0];
        var rightBody = new ReplaceParameter(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(op(left.Body, rightBody), parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == from ? to : base.VisitParameter(node);
    }
}
```

## 4. The rest of the catalogue: only what's specific here

The model knows these patterns. This table holds only what this codebase and its rules add, which is
where a general answer goes wrong.

| Pattern | What's specific here |
|---|---|
| Factory | A DI factory delegate or `ActivatorUtilities`; a factory class only when creation needs runtime data DI doesn't have. Never one that takes `IServiceProvider` and resolves by name |
| Builder | Test-data builders in test projects. In production, a record with `required`/`init`; a builder that stays must validate in `Build()` |
| Singleton | `AddSingleton` only; never a static `Instance`. No scoped dependency inside (captive dependency, `rules/csharp-standards.md`) |
| Adapter / anti-corruption layer | Every vendor client (payment gateway, SMS, national ID): the interface uses our types and our result, the adapter translates. Vendor DTOs stay `internal` to Infrastructure |
| Facade | Only over a workflow that always calls the same services in order; a facade that forwards method for method is a layer, not a simplification |
| Proxy | `DelegatingHandler`, generated gRPC clients. EF lazy-loading proxies are banned |
| Composite | Nested rule groups (AND/OR), menus, permission trees; not flat lists |
| Chain of Responsibility | Middleware, endpoint filters, `DelegatingHandler` first. Our own chain only when the order is a business rule, registered in one place |
| Observer | In process: domain events dispatched after `SaveChanges` (`ddd-patterns`). Across services: Kafka through the outbox (`outbox`). Never a singleton C# event that scoped objects subscribe to |
| Command | CQRS commands (`cqrs-eventsourcing`), Hangfire/Quartz jobs: data only, so they can be logged and retried |
| Template Method | `BackgroundService` is one. For our own code, compose (inject the varying step); no base-class hierarchies for reuse |
| Mediator | Not by default: handlers are injected directly (`cqrs-eventsourcing`); MediatR 13+ is commercial, and only when asked for by name (`package-policy`) |
| Repository | One per aggregate, interface in Domain, intent-named async methods; no generic `IRepository<T>` over EF, no `IQueryable` out of Infrastructure. Reads go through Dapper or projections |
| Unit of Work | **Depends on the layering.** Clean Architecture: Application can't see the `DbContext`, so a thin `IUnitOfWork` port **fits** (better when it owns the transaction, `ExecuteInTransactionAsync`). A simple service whose handlers use the `DbContext`: the wrapper is **over-engineered**. Check before judging (the evals caught the old rule) |
| Result | Expected failures (not found, not allowed, invalid) are results; bugs stay exceptions (`rules/architecture.md`) |
| Saga | Choreography over Kafka for two or three steps; orchestration with persisted state for longer flows. Idempotent steps; no 2PC |
| Strangler Fig | Route by route behind YARP; no "temporary" dual-write between the old and new database |

Outbox and idempotent consumers → `outbox`. CQRS → `cqrs-eventsourcing`. Aggregates, value objects,
domain events → `ddd-patterns`. Retry, circuit breaker, bulkhead → `polly-resilience`. Cache-aside →
`caching`.

## 5. Misuses to flag in review

Each is a **Misused** or **Built into .NET** finding when the evidence is in the diff:

1. A static `Instance` property or a hand-rolled lazy singleton.
2. `IServiceProvider` injected into business code to resolve things (service locator; banned).
3. A generic repository over EF Core, or a repository method returning `IQueryable<T>` out of Infrastructure.
4. A factory that only calls `new`.
5. An interface with one implementation and no I/O, created "for testability" of pure logic.
6. The same `switch` on the same type in three or more places (**Missing**: strategy).
7. Status kept in several `bool` flags that can contradict each other (**Missing**: state).
8. Inheritance used to share code between classes that aren't the same kind of thing.
9. A singleton subscribing scoped objects to a C# event.
10. A decorator that swallows exceptions or changes the result's meaning.
11. A specification combined with `Expression.Invoke`, or compiled before `Where` on a `DbSet`.
12. A builder for a type with a handful of fields.
13. Publishing an event in the same method that commits the transaction, without an outbox.

## 6. Explaining a pattern (for the Explain mode)

When explaining a pattern found in code, say:
1. **The name**, and the one-sentence problem it solves.
2. **How it works here:** the interface, the implementations, where they're registered, who calls them.
   Use the real symbol names. Use Roslyn for the facts (`find_implementations`, `get_type_hierarchy`,
   `find_callers`); don't infer them from file names.
3. **The verdict** (§1) with the evidence.
4. **What you'd lose** by removing it, or **what it costs** to keep it. One line each.

Keep it about this code. A textbook definition the reader can search for is padding.
