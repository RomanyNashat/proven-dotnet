---
name: grpc-patterns
description: gRPC for .NET — int ids and enums in the proto, error mapping that keeps NotFound and DeadlineExceeded, a default client deadline, retries in the service config (not the HTTP resilience handler), streaming with cancellation, money without decimal. Tested in CI over real HTTP/2.
version: 2.1.0
---

# gRPC Patterns

What's specific here, and where the common example goes wrong. The code marked as a sample runs in CI
(`tests/SkillSamples.Tests/Grpc`): a real Kestrel server on localhost over HTTP/2, a real client, and
tests for every claim marked *tested*.

## 1. When gRPC

Service-to-service calls inside the platform: typed contracts, HTTP/2, streaming. Anything a browser, the
mobile app or a third party calls stays REST (`api-design`). gRPC errors don't show up as HTTP status
codes, so ingress logs and APM need the gRPC status (`observability`).

## 2. The contract

<!-- sample: tests/SkillSamples.Tests/Grpc/orders.proto -->
```protobuf
syntax = "proto3";

package orders.v1;

option csharp_namespace = "SkillSamples.GrpcSamples.V1";

import "google/protobuf/timestamp.proto";

service Orders {
  rpc GetOrder (GetOrderRequest) returns (OrderReply);
  rpc CreateOrder (CreateOrderRequest) returns (OrderReply);
  rpc WatchOrder (WatchOrderRequest) returns (stream OrderStatusChanged);
}

// Keys are int (column rules): int32, not a string to parse.
message GetOrderRequest {
  int32 order_id = 1;
}

message CreateOrderRequest {
  int32 customer_id = 1;
  repeated OrderLine lines = 2;
  optional string notes = 3;
}

message OrderLine {
  int32 product_id = 1;
  int32 quantity = 2;
}

message OrderReply {
  int32 id = 1;
  OrderStatus status = 2;
  Money total = 3;
  google.protobuf.Timestamp created_at = 4;
}

// Zero is "not set": a client built before a new value sees 0, not a wrong status.
enum OrderStatus {
  ORDER_STATUS_UNSPECIFIED = 0;
  ORDER_STATUS_PENDING = 1;
  ORDER_STATUS_PAID = 2;
  ORDER_STATUS_CANCELLED = 3;
}

// Protobuf has no decimal. Same shape as google.type.Money: nanos has the sign of units.
message Money {
  int64 units = 1;
  int32 nanos = 2;
  string currency = 3;
}

message WatchOrderRequest {
  int32 order_id = 1;
}

message OrderStatusChanged {
  int32 order_id = 1;
  OrderStatus status = 2;
  google.protobuf.Timestamp changed_at = 3;
}
```

- **Ids are `int32`** (column rule: `int` keys). A string id that the server parses turns a bad id into
  `FormatException`, which leaves as Internal.
- **Enums start with `_UNSPECIFIED = 0`.** A client built before a new value reads it as 0, not as a
  wrong status.
- **Never reuse or renumber a field.** Remove a field by `reserved 4;` and its name; add fields with new
  numbers. Changing a field's type breaks every client already deployed.
- Version the package (`orders.v1`). A breaking change is a new package beside the old one.

## 3. The server

<!-- sample: tests/SkillSamples.Tests/Grpc/OrdersService.cs -->
```csharp
public sealed class OrdersService(IOrderStore store, TimeProvider time) : Orders.OrdersBase
{
    public override async Task<OrderReply> GetOrder(GetOrderRequest request, ServerCallContext context)
    {
        var order = await store.FindAsync(request.OrderId, context.CancellationToken)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "Order not found."));   // no ids or details in the text
        return ToReply(order);
    }

    public override async Task<OrderReply> CreateOrder(CreateOrderRequest request, ServerCallContext context)
    {
        if (request.Lines.Count == 0 || request.Lines.Any(l => l.Quantity is < 1 or > 100))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "An order needs lines with a quantity from 1 to 100."));
        }

        var total = request.Lines.Sum(l => l.Quantity * 10m);
        var order = await store.AddAsync(request.CustomerId, total, time.GetUtcNow(), context.CancellationToken);
        return ToReply(order);
    }

    // Server streaming: runs until the client cancels or its deadline passes; the token carries both.
    public override async Task WatchOrder(WatchOrderRequest request, IServerStreamWriter<OrderStatusChanged> responseStream, ServerCallContext context)
    {
        await foreach (var change in store.WatchAsync(request.OrderId, context.CancellationToken))
        {
            await responseStream.WriteAsync(change, context.CancellationToken);
        }
    }

    private static OrderReply ToReply(StoredOrder order) => new()
    {
        Id = order.Id,
        Status = order.Status,
        Total = order.Total.ToMoney("SAR"),
        CreatedAt = Timestamp.FromDateTimeOffset(order.CreatedAt),
    };
}
```

```csharp
builder.Services.AddGrpc(o => o.Interceptors.Add<ErrorInterceptor>());   // EnableDetailedErrors stays off
app.MapGrpcService<OrdersService>();
```
- Pass `context.CancellationToken` to every await: it fires when the client cancels **and** when the
  deadline passes.
- The status text goes to the caller: no ids, names or values in it (`rules/security.md`). Every lookup by
  id still checks the caller may see that record.

## 4. Errors

<!-- sample: tests/SkillSamples.Tests/Grpc/ErrorInterceptor.cs -->
```csharp
/// <summary>
/// Turns exceptions the service didn't expect into a status the client can act on, without their text.
/// An RpcException is the service's own answer (NotFound, InvalidArgument) and passes through untouched.
/// A cancelled call says why: DeadlineExceeded if the deadline passed, Cancelled if the client gave up.
/// Left alone it reaches the client as Unknown; mapped to Cancelled every time, it hides the deadline.
/// </summary>
public sealed class ErrorInterceptor(ILogger<ErrorInterceptor> logger, TimeProvider time) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            throw Map(ex, context);
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            throw Map(ex, context);
        }
    }

    private RpcException Map(Exception ex, ServerCallContext context)
    {
        if (ex is OperationCanceledException && context.CancellationToken.IsCancellationRequested)
        {
            return context.Deadline <= time.GetUtcNow().UtcDateTime
                ? new RpcException(new Status(StatusCode.DeadlineExceeded, "The deadline passed."))
                : new RpcException(new Status(StatusCode.Cancelled, "The call was cancelled."));
        }

        if (ex is ValidationException validation)
        {
            var errors = string.Join("; ", validation.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            return new RpcException(new Status(StatusCode.InvalidArgument, errors));
        }

        // The exception's message can carry patient data: it goes to the log, never to the caller.
        logger.LogError(ex, "Unhandled error in {Method}", context.Method);
        return new RpcException(new Status(StatusCode.Internal, "An internal error occurred."));
    }
}
```

Tested:
- **The service's own `RpcException` must pass through.** The common interceptor catches every exception,
  including the `NotFound` the service just threw, and turns it into Internal. Tested: through a
  catch-all interceptor, a missing order reaches the client as Internal.
- **A cancelled call has to say why.** Left alone, the `OperationCanceledException` reaches the client as
  Unknown; mapped to Cancelled every time (the old version of this skill), it hides a passed deadline
  (seen in CI: the client got Cancelled). With the check above, twenty calls in a row that pass their
  deadline each report DeadlineExceeded.
- An unexpected exception is Internal with a fixed text; its message, which can carry patient data, goes
  to the log only. Tested with a national ID in the message.
- As a story: a receptionist sends a line with quantity 0 and gets InvalidArgument with the reason, and
  nothing is stored; she fixes it, the order is created with an exact total; a lookup of an order that
  isn't there is NotFound, so the app says "no such order" rather than "try again later". On a slim image
  (no ICU, no tzdata), orders round-trip over HTTP/2 with UTC timestamps and exact money.
- Expected outcomes (not found, not allowed, conflict) are the service's decision: return them as a
  status from the method, not as exceptions to be mapped.

## 5. The client

<!-- sample: tests/SkillSamples.Tests/Grpc/OrdersClientRegistration.cs -->
```csharp
public static class OrdersClientRegistration
{
    // gRPC's own retries, from the channel's service config. Only reads: every gRPC call is an HTTP POST,
    // and a retried create could run twice.
    private static readonly MethodConfig ReadRetries = new()
    {
        Names = { new MethodName { Service = "orders.v1.Orders", Method = "GetOrder" } },
        RetryPolicy = new RetryPolicy
        {
            MaxAttempts = 3,
            InitialBackoff = TimeSpan.FromMilliseconds(100),
            MaxBackoff = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 2,
            RetryableStatusCodes = { StatusCode.Unavailable },
        },
    };

    public static IHttpClientBuilder AddOrdersClient(this IServiceCollection services, Uri address) =>
        services.AddGrpcClient<Orders.OrdersClient>(o => o.Address = address)
            .ConfigureChannel(channel => channel.ServiceConfig = new ServiceConfig { MethodConfigs = { ReadRetries } })
            .AddInterceptor(sp => new DefaultDeadlineInterceptor(sp.GetRequiredService<TimeProvider>(), TimeSpan.FromSeconds(5)));
}
```

<!-- sample: tests/SkillSamples.Tests/Grpc/DefaultDeadlineInterceptor.cs -->
```csharp
/// <summary>
/// Client side: a unary call with no deadline gets one. A gRPC call without a deadline waits forever,
/// and the server can't add one afterwards. Streams are left alone: a deadline would end them.
/// </summary>
public sealed class DefaultDeadlineInterceptor(TimeProvider time, TimeSpan timeout) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        if (context.Options.Deadline is null)
        {
            var options = context.Options.WithDeadline(time.GetUtcNow().UtcDateTime + timeout);
            context = new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
        }

        return continuation(request, context);
    }
}
```

Tested:
- **A call without a deadline gets one,** and when it passes the client gets DeadlineExceeded and the
  server's token fires too. A gRPC call with no deadline waits forever; the server can't add one.
- **Retries belong in the channel's service config.** A read that fails twice with Unavailable succeeds
  on the third attempt.
- **`AddStandardResilienceHandler()` doesn't retry gRPC errors.** gRPC answers HTTP 200 and puts the
  status in the trailers, so the HTTP pipeline sees a success: tested, one attempt, Unavailable to the
  caller. It also retries every gRPC call alike, and every gRPC call is a POST.
- Only idempotent methods get retries. A create retried after a lost reply runs twice.
- Inside a gRPC or HTTP request, `.EnableCallContextPropagation()` on the client passes the incoming
  deadline and cancellation on to the next call (not run in CI).

## 6. Streaming

The server loop in §3 runs until the client cancels or the deadline passes; both arrive through
`context.CancellationToken`. Tested: the client reads three updates and cancels; the client gets
Cancelled and the server's loop stops. A stream gets no default deadline (§5 leaves streams alone),
so a long watch needs its own end: the client's cancellation, or a deadline it sets on purpose.

## 7. Money

<!-- sample: tests/SkillSamples.Tests/Grpc/MoneyConversions.cs -->
```csharp
public static class MoneyConversions
{
    private const decimal NanosPerUnit = 1_000_000_000m;

    public static Money ToMoney(this decimal amount, string currency)
    {
        var units = decimal.Truncate(amount);   // toward zero: -12.5 is -12 units and -500,000,000 nanos
        return new Money { Units = (long)units, Nanos = (int)((amount - units) * NanosPerUnit), Currency = currency };
    }

    public static decimal ToDecimal(this Money money) => money.Units + (money.Nanos / NanosPerUnit);
}
```

Tested: 12.5, -12.5, 0.000000001 and 1,234,567.89 come back exactly, and `nanos` has the sign of
`units`. Timestamps are `google.protobuf.Timestamp` from `TimeProvider`, always UTC.

## 8. Health

`AddGrpcHealthChecks()` + `MapGrpcHealthChecksService()` expose the standard `grpc.health.v1` service,
and Kubernetes can probe it directly (`grpc:` probe, `kubernetes-dotnet`). A service that also serves
HTTP keeps `/health/live` and `/health/ready` there. `MapGrpcReflectionService()` only outside production.

## 9. Review checklist
- A string id parsed on the server; an enum without `_UNSPECIFIED = 0`; a renumbered or reused field.
- An interceptor that catches `RpcException`, or maps every `OperationCanceledException` to Cancelled.
- Exception messages or ids in the status text; `EnableDetailedErrors` outside development.
- A unary call with no deadline; `DateTime.UtcNow` for a deadline instead of `TimeProvider`.
- `AddStandardResilienceHandler()` on a gRPC client as the retry; retries on a non-idempotent method.
- A server loop or await that ignores `context.CancellationToken`.
