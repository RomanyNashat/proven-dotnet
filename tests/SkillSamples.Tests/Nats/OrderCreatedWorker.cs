using Microsoft.Extensions.Hosting;
using NATS.Client.Core;

namespace SkillSamples.Nats;

public interface IOrderCreatedHandler
{
    Task HandleAsync(OrderCreated order, CancellationToken ct);
}

// Every instance of the service runs this. The queue group gives each message to one of them; without
// it, every instance would handle every order.
public sealed class OrderCreatedWorker(INatsClient nats, IOrderCreatedHandler handler) : BackgroundService
{
    public const string Subject = "orders.created";
    public const string QueueGroup = "order-workers";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<OrderCreated>(Subject, QueueGroup, cancellationToken: stoppingToken))
        {
            if (msg.Data is { } order)
                await handler.HandleAsync(order, stoppingToken);
        }
    }
}
