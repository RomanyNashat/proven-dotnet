---
name: dotnet-core
description: .NET 8/9/10 fundamentals: minimal hosting, DI lifetimes, configuration, options pattern, modern C# features.
version: 1.0.0
---

# .NET Core & Modern C# Patterns

## C# 12 Patterns (.NET 8+)

### Primary Constructors
Use for DI injection in services. Eliminates backing field boilerplate.

```csharp
// ✅ C# 12+ — clean, concise
public sealed class OrderService(
    IOrderRepository repository,
    ILogger<OrderService> logger,
    TimeProvider timeProvider)
{
    public async Task<Order> GetAsync(int id, CancellationToken ct)
    {
        logger.LogInformation("Fetching order {OrderId}", id);
        return await repository.GetByIdAsync(id, ct)
            ?? throw new OrderNotFoundException(id);
    }
}

// ❌ Old way — unnecessary boilerplate
public sealed class OrderService
{
    private readonly IOrderRepository _repository;
    private readonly ILogger<OrderService> _logger;
    // ... constructor assigning each field
}
```

**Warning**: Primary constructor parameters are mutable. Don't reassign them. If you need readonly enforcement, use `field` keyword (C# 14) or assign to a readonly field explicitly.

### Collection Expressions
```csharp
// ✅ C# 12+
int[] ids = [1, 2, 3];
List<string> names = ["Alice", "Bob"];
ReadOnlySpan<byte> header = [0x48, 0x54, 0x54, 0x50];
ImmutableArray<int> immutable = [10, 20, 30];

// Spread operator
int[] combined = [..firstArray, ..secondArray, 99];

// Empty collections
List<Order> empty = [];
```

### Records for DTOs and Value Objects
```csharp
// DTO — immutable data carrier
public sealed record OrderDto(
    int Id,
    string CustomerName,
    decimal Total,
    OrderStatus Status,
    DateTimeOffset CreatedAt);

// Value Object — domain concept with equality by value
public sealed record Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);
    public Money Add(Money other)
    {
        if (Currency != other.Currency)
            throw new CurrencyMismatchException(Currency, other.Currency);
        return this with { Amount = Amount + other.Amount };
    }
}

// Positional deconstruction
var (amount, currency) = price;
```

### Required Members
```csharp
public class CreateOrderCommand
{
    public required int CustomerId { get; init; }
    public required List<OrderLineItem> Items { get; init; }
    public string? Notes { get; init; }  // optional
}

// Compiler enforces: new CreateOrderCommand { CustomerId = ..., Items = ... }
```

### Raw String Literals
```csharp
var sql = """
    SELECT o.id, o.customer_id, o.total
    FROM orders o
    WHERE o.status = @status
      AND o.created_at >= @since
    ORDER BY o.created_at DESC
    LIMIT @pageSize OFFSET @offset
    """;

var json = """
    {
        "name": "test",
        "value": 42
    }
    """;
```

### Pattern Matching (exhaustive)
```csharp
// Switch expression with pattern matching
public static string GetStatusDisplay(OrderStatus status) => status switch
{
    OrderStatus.Pending => "Awaiting processing",
    OrderStatus.Processing => "In progress",
    OrderStatus.Shipped => "On the way",
    OrderStatus.Delivered => "Completed",
    OrderStatus.Cancelled => "Cancelled",
    _ => throw new UnreachableException($"Unknown status: {status}")
};

// Property pattern
if (order is { Status: OrderStatus.Pending, Total: > 1000m })
{
    await RequireManagerApproval(order);
}

// List pattern (.NET 8+)
int[] numbers = [1, 2, 3, 4, 5];
var result = numbers switch
{
    [1, .., 5] => "starts with 1, ends with 5",
    [var first, ..] => $"starts with {first}",
    [] => "empty"
};
```

## C# 13 Patterns (.NET 9+)

### params with Collections
```csharp
// C# 13: params works with Span, ReadOnlySpan, IEnumerable, and collections
public void Log(params ReadOnlySpan<string> messages)
{
    foreach (var msg in messages)
        Console.WriteLine(msg);
}

// Called naturally
Log("hello", "world");
```

### Partial Properties
```csharp
// Useful with source generators
public partial class UserViewModel
{
    public partial string FullName { get; set; }
}

// Source generator provides implementation
public partial class UserViewModel
{
    private string _fullName = "";
    public partial string FullName
    {
        get => _fullName;
        set => SetProperty(ref _fullName, value);
    }
}
```

## C# 14 Patterns (.NET 10)

### field Keyword
```csharp
// ✅ C# 14 — no explicit backing field needed
public class Product
{
    public string Name
    {
        get;
        set => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    public decimal Price
    {
        get;
        set => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    // Read-only with lazy initialization
    public string Slug
    {
        get => field ??= Name.ToLowerInvariant().Replace(' ', '-');
    }
}
```

### Extension Blocks
```csharp
// ✅ C# 14 — extension properties, methods, operators, static members
public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> source)
    {
        // Extension property
        public bool IsEmpty => !source.Any();

        // Extension method
        public IEnumerable<T> WhereNotNull() where T : class
            => source.Where(x => x is not null);
    }

    // Static extension members
    extension<T>(List<T>)
    {
        public static List<T> Empty => [];
    }
}

// Usage — feels like native members
if (orders.IsEmpty) return NotFound();
var validItems = items.WhereNotNull().ToList();
var empty = List<Order>.Empty;
```

### Null-Conditional Assignment
```csharp
// ✅ C# 14
customer?.Address ??= new Address("Unknown");

// Equivalent to:
if (customer is not null && customer.Address is null)
    customer.Address = new Address("Unknown");
```

## .NET Runtime Patterns

### TimeProvider (use instead of DateTime)
```csharp
// Register in DI
services.AddSingleton(TimeProvider.System);

// Inject and use
public sealed class OrderService(TimeProvider timeProvider)
{
    public Order CreateOrder(CreateOrderCommand cmd)
    {
        var now = timeProvider.GetUtcNow();
        return new Order { CreatedAt = now, ExpiresAt = now.AddHours(24) };
    }
}

// In tests — fully controllable
var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 3, 18, 12, 0, 0, TimeSpan.Zero));
fakeTime.Advance(TimeSpan.FromHours(25)); // simulate expiry
```

### Guid.CreateVersion7 (.NET 9+) — only where a GUID is really needed
Database keys are `int` identity (`rules/efcore-rules.md`). When an ID must be created
outside the database — a message ID, an idempotency key, an ID handed to an external system — use a
time-ordered v7 GUID rather than `Guid.NewGuid()`:
```csharp
public sealed record OrderPlaced(int OrderId, DateTimeOffset OccurredAt)
{
    public Guid MessageId { get; init; } = Guid.CreateVersion7();   // ✅ not a DB key
}
```

### IHttpClientFactory (mandatory)
```csharp
// Registration — named client
builder.Services.AddHttpClient("PaymentGateway", client =>
{
    client.BaseAddress = new Uri("https://api.payments.example.com");
    client.Timeout = TimeSpan.FromSeconds(10);
})
.AddStandardResilienceHandler();  // Polly v8 built-in

// Registration — typed client
builder.Services.AddHttpClient<IPaymentClient, PaymentClient>();

// Usage
public sealed class PaymentClient(HttpClient httpClient) : IPaymentClient
{
    public async Task<PaymentResult> ChargeAsync(PaymentRequest req, CancellationToken ct)
        => await httpClient.PostAsJsonAsync("/v1/charges", req, ct)
            .EnsureSuccessStatusCode()
            .Content.ReadFromJsonAsync<PaymentResult>(ct);
}
```

### Keyed DI Services (.NET 8+)
```csharp
// Registration
builder.Services.AddKeyedSingleton<INotificationSender, EmailSender>("email");
builder.Services.AddKeyedSingleton<INotificationSender, SmsSender>("sms");
builder.Services.AddKeyedSingleton<INotificationSender, PushSender>("push");

// Injection
public sealed class NotificationService(
    [FromKeyedServices("email")] INotificationSender emailSender,
    [FromKeyedServices("sms")] INotificationSender smsSender)
{
    // ...
}
```

### Global Using Directives
```csharp
// GlobalUsings.cs — one file per project
global using System.Collections.Immutable;
global using Microsoft.Extensions.Logging;
global using FluentAssertions;  // in test projects only — pin 7.x: [7.0.0,8.0.0)
global using Moq;               // in test projects only
```

## Anti-Patterns to Avoid

```csharp
// ❌ Sync over async — deadlocks in ASP.NET Core
var result = GetDataAsync().Result;
var result2 = GetDataAsync().GetAwaiter().GetResult();

// ❌ Direct HttpClient — socket exhaustion
using var client = new HttpClient();

// ❌ DateTime.Now — untestable
var now = DateTime.UtcNow;

// ❌ String concatenation for SQL — injection risk
var sql = $"SELECT * FROM users WHERE name = '{name}'";

// ❌ Catching base Exception silently
try { DoWork(); } catch (Exception) { }

// ❌ BinaryFormatter — RCE vulnerability
BinaryFormatter formatter = new();

## See also
- **`aspire-patterns`** — when a solution needs orchestrated local composition (AppHost, service
  discovery, ServiceDefaults) instead of hand-wired connection strings per service.
