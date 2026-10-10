using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Kafka;

public sealed record AppointmentBooked(int AppointmentId, int Sequence);

public sealed class RecordingHandler : IKafkaMessageHandler
{
    public ConcurrentQueue<string> Handled { get; } = new();
    public ConcurrentDictionary<string, int> Attempts { get; } = new();
    public Func<string, int, CancellationToken, Task>? Behaviour { get; set; }

    public async Task HandleAsync(ConsumeResult<string, string> message, CancellationToken ct)
    {
        var attempt = Attempts.AddOrUpdate(message.Message.Value, 1, (_, n) => n + 1);
        if (Behaviour is not null)
        {
            await Behaviour(message.Message.Value, attempt, ct);
        }

        Handled.Enqueue(message.Message.Value);
    }
}

[Collection("kafka")]
public sealed class KafkaTests : IDisposable
{
    private static readonly string Bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "localhost:9092";
    private readonly IProducer<string, string> _producer = new ProducerBuilder<string, string>(KafkaPublisher.Config(Bootstrap)).Build();
    private readonly string _topic = $"samples.appointment.booked.{Guid.NewGuid():N}";
    private readonly string _group = $"samples-{Guid.NewGuid():N}";

    public void Dispose() => _producer.Dispose();

    private async Task PublishAsync(params string[] values)
    {
        var publisher = new KafkaPublisher(_producer);
        foreach (var value in values)
        {
            await _producer.ProduceAsync(_topic, new Message<string, string> { Key = "appointment-7", Value = value });
        }

        await publisher.PublishAsync(_topic, "appointment-7", new AppointmentBooked(7, 99), default);   // typed path, last
    }

    private IHost Worker(RecordingHandler handler)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new KafkaConsumerOptions
        {
            BootstrapServers = Bootstrap, GroupId = _group, Topic = _topic, MaxAttempts = 3, RetryDelay = TimeSpan.FromMilliseconds(50),
        });
        builder.Services.AddSingleton(_producer);
        builder.Services.AddSingleton<IKafkaMessageHandler>(handler);
        builder.Services.AddHostedService<KafkaConsumerWorker>();
        return builder.Build();
    }

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(100);
        }

        Assert.True(condition(), $"timed out waiting for: {what}");
    }

    [Fact]
    public async Task TransientFailure_IsRetried_InOrder_AndNotRedeliveredAfterRestart()
    {
        await PublishAsync("m1", "m2", "m3");
        var handler = new RecordingHandler
        {
            Behaviour = (value, attempt, _) => value == "m2" && attempt < 3 ? throw new TimeoutException("db busy") : Task.CompletedTask,
        };

        using (var host = Worker(handler))
        {
            await host.StartAsync();
            await WaitUntil(() => handler.Handled.Count == 4, "four messages handled");
            await host.StopAsync();
        }

        Assert.Equal(new[] { "m1", "m2", "m3" }, handler.Handled.Take(3).ToArray());
        Assert.Equal(3, handler.Attempts["m2"]);

        var second = new RecordingHandler();
        using (var host = Worker(second))
        {
            await host.StartAsync();
            await Task.Delay(TimeSpan.FromSeconds(5));
            await host.StopAsync();
        }

        Assert.Empty(second.Handled);   // offsets were committed on close
    }

    [Fact]
    public async Task PoisonMessage_GoesToTheDeadLetterTopic_AndTheRestFlows()
    {
        await PublishAsync("good-1", "poison", "good-2");
        var handler = new RecordingHandler
        {
            Behaviour = (value, _, _) => value == "poison" ? throw new FormatException("bad payload") : Task.CompletedTask,
        };

        using (var host = Worker(handler))
        {
            await host.StartAsync();
            await WaitUntil(() => handler.Handled.Count == 3, "the good messages handled");
            await host.StopAsync();
        }

        Assert.Equal(new[] { "good-1", "good-2" }, handler.Handled.Take(2).ToArray());
        Assert.Equal(3, handler.Attempts["poison"]);

        using var dlq = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Bootstrap, GroupId = $"{_group}-dlq", AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        dlq.Subscribe($"{_topic}.dlq");
        var parked = dlq.Consume(TimeSpan.FromSeconds(30));
        dlq.Close();

        Assert.NotNull(parked);
        Assert.Equal("poison", parked.Message.Value);
        Assert.Equal("System.FormatException", Encoding.UTF8.GetString(parked.Message.Headers.GetLastBytes("dlq-error")));
    }

    [Fact]
    public async Task StopMidMessage_ThatMessageIsRedelivered_EarlierOnesAreNot()
    {
        await PublishAsync("m1", "m2", "m3");
        var first = new RecordingHandler
        {
            Behaviour = (value, _, ct) => value == "m2" ? Task.Delay(Timeout.Infinite, ct) : Task.CompletedTask,
        };

        using (var host = Worker(first))
        {
            await host.StartAsync();
            await WaitUntil(() => first.Attempts.ContainsKey("m2"), "m2 in progress");
            await host.StopAsync();   // the pod is stopped while m2 is being handled
        }

        Assert.Equal(new[] { "m1" }, first.Handled.ToArray());

        var second = new RecordingHandler();
        using (var host = Worker(second))
        {
            await host.StartAsync();
            await WaitUntil(() => second.Handled.Count == 3, "m2, m3 and the typed event");
            await host.StopAsync();
        }

        Assert.Equal("m2", second.Handled.First());   // replayed, not lost; m1 not replayed
        Assert.DoesNotContain("m1", second.Handled);
    }

    private ConsumeResult<string, string>? ReadParked()
    {
        using var dlq = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = Bootstrap, GroupId = $"{_group}-dlq-{Guid.NewGuid():N}", AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        dlq.Subscribe($"{_topic}.dlq");
        var parked = dlq.Consume(TimeSpan.FromSeconds(30));
        dlq.Close();
        return parked;
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ALabResultCantBeReadUntilAFixShips_ItWaitsOnTheDeadLetterTopic_ThenIsReplayedOnce()
    {
        // Given: three lab results, the second in a shape the service can't read yet
        await PublishAsync("result-1", "result-2", "result-3");
        var canRead2 = false;
        var handler = new RecordingHandler
        {
            Behaviour = (value, _, _) => value == "result-2" && !canRead2 ? throw new FormatException("unknown unit") : Task.CompletedTask,
        };

        using var host = Worker(handler);
        await host.StartAsync();
        await WaitUntil(() => handler.Handled.Count == 3, "results 1, 3 and the typed event");

        // When: it's parked, the fix ships, and an operator puts the parked copy back on the topic
        var parked = ReadParked();
        Assert.NotNull(parked);
        canRead2 = true;
        await _producer.ProduceAsync(_topic, new Message<string, string> { Key = parked.Message.Key, Value = parked.Message.Value });
        await WaitUntil(() => handler.Handled.Contains("result-2"), "the replayed result");
        await host.StopAsync();

        // Then: the others didn't wait for it, it was handled once after the fix, and the parked copy said
        // where it came from without carrying the error text (which can hold patient data)
        Assert.Equal(new[] { "result-1", "result-3" }, handler.Handled.Take(2).ToArray());
        Assert.Single(handler.Handled, v => v == "result-2");
        Assert.StartsWith($"{_topic}[", Encoding.UTF8.GetString(parked.Message.Headers.GetLastBytes("dlq-source")), StringComparison.Ordinal);
        Assert.Equal("System.FormatException", Encoding.UTF8.GetString(parked.Message.Headers.GetLastBytes("dlq-error")));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcuNoTzdata_ArabicKeysAndValuesFlow_AndTheEventTypeHeaderIsSet()
    {
        ProductionConditions.Require();
        await _producer.ProduceAsync(_topic, new Message<string, string> { Key = "موعد-٧", Value = "تم الحجز" });
        await new KafkaPublisher(_producer).PublishAsync(_topic, "موعد-٧", new AppointmentBooked(7, 1), default);
        var handler = new RecordingHandler();

        using var host = Worker(handler);
        await host.StartAsync();
        await WaitUntil(() => handler.Handled.Count == 2, "both messages");
        await host.StopAsync();

        Assert.Equal("تم الحجز", handler.Handled.First());
        Assert.Equal(new AppointmentBooked(7, 1), System.Text.Json.JsonSerializer.Deserialize<AppointmentBooked>(handler.Handled.Last()));
    }
}
