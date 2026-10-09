using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using SkillSamples.GrpcSamples.V1;

namespace SkillSamples.GrpcSamples;

public sealed record StoredOrder(int Id, int CustomerId, OrderStatus Status, decimal Total, DateTimeOffset CreatedAt);

public interface IOrderStore
{
    Task<StoredOrder?> FindAsync(int id, CancellationToken ct);

    Task<StoredOrder> AddAsync(int customerId, decimal total, DateTimeOffset now, CancellationToken ct);

    IAsyncEnumerable<OrderStatusChanged> WatchAsync(int id, CancellationToken ct);
}

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
