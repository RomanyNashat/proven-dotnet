using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkillSamples.Kafka;

public sealed record KafkaConsumerOptions
{
    public required string BootstrapServers { get; init; }
    public required string GroupId { get; init; }
    public required string Topic { get; init; }
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(1);
}

public interface IKafkaMessageHandler
{
    Task HandleAsync(ConsumeResult<string, string> message, CancellationToken ct);
}

// At-least-once: a message's offset is stored only after it was handled (or parked on the dead-letter
// topic), and stored offsets are committed in the background, on rebalance and on close. A crash replays
// what wasn't stored, so handlers must be idempotent (the inbox in the outbox skill).
public sealed class KafkaConsumerWorker(
    KafkaConsumerOptions options,
    IProducer<string, string> producer,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();   // Consume() blocks; on .NET 8 don't block host startup with it
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,        // commits what was stored, every 5 s
            EnableAutoOffsetStore = false,  // ...and only we store, after handling
        };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(options.Topic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result = consumer.Consume(stoppingToken);
                await HandleWithRetryAsync(result, stoppingToken);
                consumer.StoreOffset(result);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down; the message in hand (if any) wasn't stored and will be redelivered
        }
        finally
        {
            consumer.Close();   // commits the stored offsets and leaves the group, so partitions move at once
        }
    }

    // Retries stay short: the whole loop must finish well inside max.poll.interval.ms (5 minutes by
    // default) or the broker assumes the consumer died and hands its partitions to another pod.
    private async Task HandleWithRetryAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IKafkaMessageHandler>().HandleAsync(result, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < options.MaxAttempts)
            {
                logger.LogWarning("Attempt {Attempt} failed for {Topic}[{Partition}]@{Offset}: {Error}",
                    attempt, result.Topic, result.Partition.Value, result.Offset.Value, ex.GetType().Name);
                await Task.Delay(options.RetryDelay * attempt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Out of attempts: park it and move on, so one bad message doesn't stop the partition.
                // If this write fails it throws, the offset isn't stored, and the message comes back.
                await producer.ProduceAsync($"{result.Topic}.dlq", new Message<string, string>
                {
                    Key = result.Message.Key,
                    Value = result.Message.Value,
                    Headers = new Headers
                    {
                        { "dlq-source", Encoding.UTF8.GetBytes($"{result.Topic}[{result.Partition.Value}]@{result.Offset.Value}") },
                        { "dlq-error", Encoding.UTF8.GetBytes(ex.GetType().FullName ?? "unknown") },   // the type, not the message: it can hold patient data
                    },
                }, ct);
                logger.LogError("Parked {Topic}[{Partition}]@{Offset} on the dead-letter topic after {Attempts} attempts: {Error}",
                    result.Topic, result.Partition.Value, result.Offset.Value, attempt, ex.GetType().Name);
                return;
            }
        }
    }
}
