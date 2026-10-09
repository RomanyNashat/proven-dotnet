using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace SkillSamples.Kafka;

public sealed class KafkaPublisher(IProducer<string, string> producer)
{
    public static ProducerConfig Config(string bootstrapServers) => new()
    {
        BootstrapServers = bootstrapServers,
        EnableIdempotence = true,   // the client's own retries can't duplicate or reorder within a partition
        Acks = Acks.All,
        LingerMs = 5,
        CompressionType = CompressionType.Lz4,
        MessageTimeoutMs = 30_000,  // bounds every internal retry; don't wrap ProduceAsync in a Polly retry too
    };

    // The key picks the partition, and order is kept only within a partition. Key by the aggregate
    // (the appointment id), never a random value, or one appointment's events can arrive out of order.
    public Task<DeliveryResult<string, string>> PublishAsync<TEvent>(string topic, string key, TEvent @event, CancellationToken ct) =>
        producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = JsonSerializer.Serialize(@event),
            Headers = new Headers { { "event-type", Encoding.UTF8.GetBytes(typeof(TEvent).Name) } },
        }, ct);
}
