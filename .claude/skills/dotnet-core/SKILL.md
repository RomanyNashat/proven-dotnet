---
name: dotnet-core
description: .NET 8/9/10 fundamentals — the host and DI lifetimes checked in every environment, validated options that stop a bad deploy at start-up, typed HTTP clients, keyed services, TimeProvider, and modern C# (records, required, field, extension blocks). Tested in CI, also under slim-image conditions.
version: 2.0.0
---

# .NET Core & Modern C# Patterns

## The host: check service lifetimes in every environment

<!-- sample: tests/SkillSamples.Tests/Core/ServiceValidation.cs -->
```csharp
public static class ServiceValidation
{
    // The host checks service lifetimes only in Development. In Production a singleton that takes a scoped
    // service (a DbContext, the current user) builds fine and then shares that one instance across every
    // request. Turn both checks on everywhere: a broken lifetime then stops the deploy at start-up.
    // ValidateOnBuild alone doesn't catch it; it needs ValidateScopes too.
    public static TBuilder ValidateServicesInEveryEnvironment<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
        return builder;
    }
}
```

```csharp
var builder = WebApplication.CreateBuilder(args).ValidateServicesInEveryEnvironment();
```

Tested as a story: a singleton that takes a scoped service, run with the Production defaults, gives two
requests the **same** "scoped" instance. For a DbContext that means two users' requests on one context
(not thread-safe, and one user's tracked entities in the other's request). With both checks on, the host
refuses to build. `ValidateOnBuild` alone builds it fine (tested).

- With `ValidateScopes` on, resolving a scoped service from the root provider throws
  (`app.Services.GetRequiredService<OrdersDbContext>()` in `Program.cs`). That code was already wrong: the
  instance lived for the whole process. Create a scope (`app.Services.CreateScope()`) instead.
- Lifetimes: **scoped** for DbContext, repositories and the current user; **singleton** for clients that
  pool connections (Redis multiplexer, MongoClient) and for stateless services; **transient** for
  lightweight, stateless helpers. A singleton that needs a scoped service takes `IServiceScopeFactory`.

## Options: validated at start-up

<!-- sample: tests/SkillSamples.Tests/Core/ShippingOptions.cs -->
```csharp
public sealed class ShippingOptions
{
    public const string Section = "Shipping";

    // `required` only binds the compiler: when the setting is missing, the binder leaves this null.
    // [Required] is what catches it, at start-up (ValidateOnStart).
    [Required]
    public required Uri BaseAddress { get; init; }
}
```

<!-- sample: tests/SkillSamples.Tests/Core/ShippingSetup.cs -->
```csharp
public static class ShippingSetup
{
    public static IHttpClientBuilder AddShipping(this IServiceCollection services)
    {
        services.AddOptions<ShippingOptions>()
            .BindConfiguration(ShippingOptions.Section)
            .ValidateDataAnnotations()
            .Validate(o => o.BaseAddress is null || o.BaseAddress.AbsolutePath.EndsWith('/'),
                "Shipping:BaseAddress must end with '/', or its last path segment is dropped from every call.")
            .ValidateOnStart();   // a bad setting stops the deploy, not the first shipment

        // A typed client from IHttpClientFactory; retries and timeouts come from `polly-resilience`.
        return services.AddHttpClient<ShippingClient>((provider, client) =>
            client.BaseAddress = provider.GetRequiredService<IOptions<ShippingOptions>>().Value.BaseAddress);
    }
}
```

Tested as stories: a deploy that lost the `Shipping` section stops at start-up with
`OptionsValidationException` naming `BaseAddress`; a base address without its trailing `/` stops it too.

- **`required` doesn't protect options.** The configuration binder doesn't go through the compiler, so a
  missing setting leaves the property null. Validate it (`[Required]`, `Validate(...)`), and write custom
  checks so they don't throw on null: every validator runs, and a `NullReferenceException` from one hides
  the clear message from the others.
- `ValidateOnStart()` on every options class the service can't run without. Without it, validation runs
  the first time something asks for the options, which is a request in production.
- No URLs in code: base addresses come from configuration (`aspnetcore-rules`).

## HTTP clients: typed, from IHttpClientFactory

<!-- sample: tests/SkillSamples.Tests/Core/ShippingClient.cs -->
```csharp
public sealed record Shipment(int OrderId, string TrackingNumber);

public sealed class ShippingClient(HttpClient http)
{
    // A relative path with no leading '/': "/shipments" would drop the base address's path ("/api/").
    public async Task<Shipment> CreateAsync(int orderId, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("shipments", new { orderId }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Shipment>(ct)
            ?? throw new InvalidOperationException("The shipping service answered with an empty body.");
    }
}
```

**The base address keeps its path only when it ends with `/` and the call's path doesn't start with
one.** Tested: `https://shipping.example.com/api/` with `"/shipments"`, and `https://shipping.example.com/api`
with `"shipments"`, both call `https://shipping.example.com/shipments`. The options check above catches the first
half; relative paths in the client the second.

- Never `new HttpClient()` (socket exhaustion, stale DNS). Typed clients as above, or named clients.
- Retries, timeouts and the circuit breaker: `polly-resilience`. Leave `HttpClient.Timeout` to it (its
  sample sets `Timeout.InfiniteTimeSpan`); the default is 100 seconds.

## Keyed services (.NET 8+)

<!-- sample: tests/SkillSamples.Tests/Core/Notifications.cs -->
```csharp
public interface INotificationSender
{
    string Channel { get; }
}

public sealed class EmailSender : INotificationSender { public string Channel => "email"; }
public sealed class SmsSender : INotificationSender { public string Channel => "sms"; }

public sealed class AppointmentReminders(
    [FromKeyedServices("email")] INotificationSender email,
    [FromKeyedServices("sms")] INotificationSender sms)
{
    public IReadOnlyList<string> Channels => [email.Channel, sms.Channel];
}

public static class NotificationSetup
{
    public static IServiceCollection AddNotifications(this IServiceCollection services) =>
        services
            .AddKeyedSingleton<INotificationSender, EmailSender>("email")
            .AddKeyedSingleton<INotificationSender, SmsSender>("sms")
            .AddSingleton<AppointmentReminders>();
}
```

Tested as a story: with lifetime checks on, removing the `sms` registration fails the host at start-up
instead of at the first reminder.

## TimeProvider instead of DateTime

<!-- sample: tests/SkillSamples.Tests/Core/SlotHold.cs -->
```csharp
// Time comes from an injected TimeProvider, so a test can move the clock instead of waiting.
public sealed class SlotHold
{
    public static readonly TimeSpan HoldFor = TimeSpan.FromMinutes(15);

    private SlotHold(int slotId, DateTimeOffset expiresAt) => (SlotId, ExpiresAt) = (slotId, expiresAt);

    public int SlotId { get; }
    public DateTimeOffset ExpiresAt { get; }   // stored and compared in UTC

    // What the patient sees. RiyadhTime works on images without tzdata (`localization` §7).
    public DateTimeOffset ExpiresAtInRiyadh => TimeZoneInfo.ConvertTime(ExpiresAt, RiyadhTime.Zone);

    public static SlotHold Place(int slotId, TimeProvider time) => new(slotId, time.GetUtcNow() + HoldFor);

    public bool HasLapsed(TimeProvider time) => time.GetUtcNow() >= ExpiresAt;
}
```

Register `services.AddSingleton(TimeProvider.System)`; tests use `FakeTimeProvider`
(`Microsoft.Extensions.TimeProvider.Testing`). Tested as a story: a hold is still there a second before
fifteen minutes and gone at fifteen. Tested with no tzdata: a hold placed at 21:50 UTC shows 01:05 the
next day in Riyadh.

## Modern C#

### Records for DTOs and value objects

<!-- sample: tests/SkillSamples.Tests/Core/Money.cs -->
```csharp
// A value object: equal by value, immutable, and it refuses to mix currencies.
public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public Money Add(Money other) =>
        other.Currency == Currency
            ? this with { Amount = Amount + other.Amount }
            : throw new InvalidOperationException($"Can't add {other.Currency} to {Currency}.");
}
```

Tested: `new Money(1.0m, "SAR")` equals `new Money(1.00m, "SAR")` (same hash too), but they print
differently (`1.0`, `1.00`). Compare values, never their strings.

### Required members

<!-- sample: tests/SkillSamples.Tests/Core/PlaceOrder.cs -->
```csharp
public sealed record OrderLine(int ProductId, int Quantity);

// `required` is checked by the compiler and by System.Text.Json: a request body without "lines" fails to
// deserialize (a 400) instead of reaching the handler with a null list.
public sealed record PlaceOrder
{
    public required int CustomerId { get; init; }
    public required IReadOnlyList<OrderLine> Lines { get; init; }
    public string? Notes { get; init; }
}
```

Tested: `{"customerId":1}` throws `JsonException` naming `lines`. It checks the property is present, not
that the list has items: that's the validator's job (FluentValidation, `api-design`).

### `field` keyword (C# 14)

<!-- sample: tests/SkillSamples.Tests/Core/Product.cs -->
```csharp
// C# 14 `field`: validation in the setter without declaring a backing field.
public sealed class Product
{
    public required string Name
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A product needs a name.", nameof(value))
            : value.Trim();
    }

    public decimal Price
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A price can't be negative.");
    }
}
```

Don't cache a derived value with `get => field ??= ...` when what it's derived from can change: the
cached value goes stale (a slug that keeps the old name).

### Extension blocks (C# 14)

<!-- sample: tests/SkillSamples.Tests/Core/EnumerableExtensions.cs -->
```csharp
// C# 14 extension blocks: extension properties as well as methods. A constraint goes on the block,
// not on a member.
public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        public bool IsEmpty => !source.Any();
    }

    extension<T>(IEnumerable<T?> source) where T : class
    {
        public IEnumerable<T> WhereNotNull() => source.OfType<T>();
    }
}
```

### Smaller features

```csharp
// Primary constructors for DI. The parameters are captured, not readonly fields: don't assign to them.
public sealed class OrderService(IOrderRepository repository, ILogger<OrderService> logger) { /* ... */ }

// Collection expressions and spread
int[] ids = [1, 2, 3];
int[] all = [..ids, 4];
List<Order> none = [];

// Raw string literals for SQL (keyset pagination, see `rules/performance.md`)
const string Sql = """
    SELECT id, customer_id, total
    FROM orders
    WHERE status = @status AND id > @afterId
    ORDER BY id
    LIMIT @pageSize
    """;

// Exhaustive switch over an enum: the discard catches values cast from an int that the enum doesn't name
public static string Describe(OrderStatus status) => status switch
{
    OrderStatus.Pending => "Awaiting processing",
    OrderStatus.Shipped => "On the way",
    OrderStatus.Delivered => "Completed",
    _ => throw new UnreachableException($"Unknown status: {status}")
};

// Null-conditional assignment (C# 14): assigns only when customer isn't null
customer?.Address ??= Address.Unknown;
```

### GUIDs: `Guid.CreateVersion7()`, and only where a GUID is really needed

Database keys are `int` identity (`rules/efcore-rules.md`). When an ID must be created outside the
database (a message ID, an idempotency key, an ID handed to another system), use a time-ordered v7 GUID
rather than `Guid.NewGuid()`:

```csharp
public sealed record OrderPlaced(int OrderId, DateTimeOffset OccurredAt)
{
    public Guid MessageId { get; init; } = Guid.CreateVersion7();   // not a database key
}
```

## Anti-patterns

```csharp
var result = GetDataAsync().Result;                  // sync over async: thread-pool starvation, deadlocks
using var client = new HttpClient();                 // socket exhaustion; use IHttpClientFactory
var now = DateTime.UtcNow;                           // untestable; inject TimeProvider
var sql = $"SELECT * FROM users WHERE name = '{name}'";   // SQL injection; parameters only
try { DoWork(); } catch (Exception) { }              // a swallowed failure
services.AddSingleton<ReportCache>();                // ...taking a scoped DbContext: see the host section
```

## Rules
- `ValidateScopes` and `ValidateOnBuild` on in every environment.
- Options bound with `ValidateDataAnnotations()`/`Validate(...)` and `ValidateOnStart()`; custom checks
  safe on null.
- Typed clients from `IHttpClientFactory`; base addresses from configuration, ending with `/`; relative
  call paths.
- `TimeProvider` for time; Riyadh time through `RiyadhTime`.
- Records for DTOs and value objects; `required` for request bodies; `IReadOnlyList<T>` in them.

## See also
- **`polly-resilience`** — retries, timeouts and circuit breakers for the typed clients here.
- **`localization`** — `RiyadhTime`, cultures, and what changes on an image without ICU.
- **`aspire-patterns`** — when a solution needs orchestrated local composition (AppHost, service
  discovery, ServiceDefaults) instead of hand-wired connection strings per service.
