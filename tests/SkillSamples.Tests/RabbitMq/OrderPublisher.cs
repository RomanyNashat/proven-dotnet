using System.Text.Json;
using RabbitMQ.Client;

namespace SkillSamples.RabbitMq;

public sealed record OrderCreated(int OrderId, decimal Total);

public sealed class OrderPublisher(IChannel channel, OrderTopology topology)
{
    // Open the channel with publisher confirmations and tracking:
    //   connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true))
    // Then PublishAsync returns once the broker has stored the message, and throws PublishException if it
    // refused it or (mandatory) no queue was bound to take it.
    public async Task PublishAsync(OrderCreated order, CancellationToken ct)
    {
        var properties = new BasicProperties
        {
            Persistent = true,                                   // written to disk, survives a broker restart
            ContentType = "application/json",
            MessageId = $"order-created-{order.OrderId}",        // consumers deduplicate on it
            Type = nameof(OrderCreated)
        };

        await channel.BasicPublishAsync(topology.Exchange, "order.created", mandatory: true, properties,
            JsonSerializer.SerializeToUtf8Bytes(order), ct);
    }
}
