using System.Text.Json;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace SkillSamples.RabbitMq;

public interface IOrderHandler
{
    Task HandleAsync(OrderCreated order, string messageId, CancellationToken ct);
}

public static class OrderConsumer
{
    // Manual acknowledgement: a message is removed only after it was handled. If the process dies first,
    // the broker delivers it again, so the handler must be idempotent (deduplicate on MessageId).
    public static async Task<string> StartAsync(
        IChannel channel, OrderTopology topology, IOrderHandler handler, ILogger logger, CancellationToken ct)
    {
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 16, global: false, ct);   // at most 16 unacked at once

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            try
            {
                var order = JsonSerializer.Deserialize<OrderCreated>(delivery.Body.Span)
                    ?? throw new JsonException("Empty message.");
                await handler.HandleAsync(order, delivery.BasicProperties.MessageId ?? "", ct);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Message {MessageId} failed; back to the queue", delivery.BasicProperties.MessageId);
                // Back to the queue. The delivery limit stops a message that always fails from looping
                // forever: after five tries it moves to the dead-letter queue for a person to look at.
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, ct);
            }
        };

        return await channel.BasicConsumeAsync(topology.Queue, autoAck: false, consumer, ct);
    }
}
