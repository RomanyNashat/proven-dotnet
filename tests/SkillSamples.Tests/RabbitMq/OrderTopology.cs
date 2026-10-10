using RabbitMQ.Client;

namespace SkillSamples.RabbitMq;

// Declared by the service at start-up (declaring what already exists with the same arguments is a no-op).
public sealed record OrderTopology(string Exchange, string Queue)
{
    public string DeadLetterExchange => $"{Exchange}.dead";
    public string DeadLetterQueue => $"{Queue}.dead";

    public async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);

        // A quorum queue (replicated) that gives up on a message after five failed deliveries and moves it
        // to the dead-letter queue. Without a dead-letter exchange it would drop it.
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = 5,
                ["x-dead-letter-exchange"] = DeadLetterExchange
            }, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, "order.created", cancellationToken: ct);

        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, "", cancellationToken: ct);
    }
}
