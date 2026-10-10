using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.RabbitMq;

public sealed class RabbitMqTests : IAsyncLifetime
{
    private static readonly Uri Url = new(Environment.GetEnvironmentVariable("RABBITMQ_URL") ?? "amqp://samples:samples@localhost:5672/");
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];
    private IConnection _connection = null!;
    private OrderTopology _topology = null!;

    public async Task InitializeAsync()
    {
        _connection = await new ConnectionFactory { Uri = Url, ClientProvidedName = "skill-samples" }.CreateConnectionAsync();
        _topology = new OrderTopology($"orders-{_run}", $"billing.order-created-{_run}");
        await using var channel = await _connection.CreateChannelAsync();
        await _topology.DeclareAsync(channel);
    }

    public async Task DisposeAsync()
    {
        await using (var channel = await _connection.CreateChannelAsync())
        {
            await channel.QueueDeleteAsync(_topology.Queue);
            await channel.QueueDeleteAsync(_topology.DeadLetterQueue);
            await channel.ExchangeDeleteAsync(_topology.Exchange);
            await channel.ExchangeDeleteAsync(_topology.DeadLetterExchange);
        }
        await _connection.DisposeAsync();
    }

    private Task<IChannel> ConfirmingChannelAsync() =>
        _connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));

    private async Task<uint> CountAsync(string queue)
    {
        await using var channel = await _connection.CreateChannelAsync();
        return (await channel.QueueDeclarePassiveAsync(queue)).MessageCount;
    }

    private async Task PublishAsync(params OrderCreated[] orders)
    {
        await using var channel = await ConfirmingChannelAsync();
        var publisher = new OrderPublisher(channel, _topology);
        foreach (var order in orders)
            await publisher.PublishAsync(order, CancellationToken.None);
    }

    private static async Task Until(Func<Task<bool>> done, TimeSpan within)
    {
        var until = DateTime.UtcNow + within;
        while (!await done() && DateTime.UtcNow < until)
            await Task.Delay(200);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ThePublisherWaitsForTheBroker_TheOrderIsStoredWhenPublishReturns()
    {
        await PublishAsync(new OrderCreated(1, 120m));

        Assert.Equal(1u, await CountAsync(_topology.Queue));   // no waiting: the confirm came first
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_NoQueueIsBoundForTheEvent_ThePublisherIsTold()
    {
        var nobodyListens = new OrderTopology($"orders-unbound-{_run}", "unused");
        await using var channel = await ConfirmingChannelAsync();
        await channel.ExchangeDeclareAsync(nobodyListens.Exchange, ExchangeType.Topic, durable: false, autoDelete: true);

        var publish = () => new OrderPublisher(channel, nobodyListens).PublishAsync(new OrderCreated(2, 50m), CancellationToken.None);

        var refused = await Assert.ThrowsAnyAsync<PublishException>(publish);
        Assert.True(refused.IsReturn);   // returned as unroutable, not silently dropped
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AConsumerDiesBeforeAcking_TheOrderIsDeliveredAgain()
    {
        await PublishAsync(new OrderCreated(3, 75m));

        // Given: one instance takes the message and dies without acknowledging it
        await using (var dying = await _connection.CreateChannelAsync())
        {
            var first = await dying.BasicGetAsync(_topology.Queue, autoAck: false);
            Assert.NotNull(first);
            Assert.False(first.Redelivered);
        }

        // Then: another instance gets the same message, marked as redelivered, with the same id
        await using var next = await _connection.CreateChannelAsync();
        var again = await next.BasicGetAsync(_topology.Queue, autoAck: false);
        Assert.NotNull(again);
        Assert.True(again.Redelivered);
        Assert.Equal("order-created-3", again.BasicProperties.MessageId);
        await next.BasicAckAsync(again.DeliveryTag, multiple: false);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AnOrderAlwaysFails_AfterItsTriesItWaitsInTheDeadLetterQueue()
    {
        var handler = new RecordingHandler(fails: true);
        await using var channel = await _connection.CreateChannelAsync();
        using var stop = new CancellationTokenSource();
        await OrderConsumer.StartAsync(channel, _topology, handler, NullLogger.Instance, stop.Token);

        await PublishAsync(new OrderCreated(4, 10m));
        await Until(async () => await CountAsync(_topology.DeadLetterQueue) == 1, TimeSpan.FromSeconds(30));

        Assert.Equal(1u, await CountAsync(_topology.DeadLetterQueue));
        Assert.Equal(0u, await CountAsync(_topology.Queue));
        Assert.InRange(handler.Attempts, 5, 6);
        await stop.CancelAsync();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AQuorumQueueWithoutADeadLetterExchange_DropsAPoisonMessageAfter20Tries()
    {
        // Given: a quorum queue declared with no delivery limit and no dead-letter exchange
        var queue = $"no-dlx-{_run}";
        await using var channel = await ConfirmingChannelAsync();
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" });
        try
        {
            await channel.BasicPublishAsync("", queue, mandatory: true, new BasicProperties { Persistent = true }, "{}"u8.ToArray());

            // When: every delivery fails
            var attempts = 0;
            await using var consuming = await _connection.CreateChannelAsync();
            var consumer = new RabbitMQ.Client.Events.AsyncEventingBasicConsumer(consuming);
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                Interlocked.Increment(ref attempts);
                await consuming.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true);
            };
            await consuming.BasicConsumeAsync(queue, autoAck: false, consumer);
            await Until(() => Task.FromResult(Volatile.Read(ref attempts) >= 20), TimeSpan.FromSeconds(30));
            await Task.Delay(1000);   // time for a 21st delivery, if there were going to be one

            // Then: it's gone, after the default limit of 20
            Assert.Equal(0u, await CountAsync(queue));
            Assert.InRange(Volatile.Read(ref attempts), 20, 21);
        }
        finally
        {
            await channel.QueueDeleteAsync(queue);
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TwoInstancesConsumeTheQueue_EachOrderIsHandledOnce()
    {
        var (a, b) = (new RecordingHandler(), new RecordingHandler());
        await using var channelA = await _connection.CreateChannelAsync();
        await using var channelB = await _connection.CreateChannelAsync();
        using var stop = new CancellationTokenSource();
        await OrderConsumer.StartAsync(channelA, _topology, a, NullLogger.Instance, stop.Token);
        await OrderConsumer.StartAsync(channelB, _topology, b, NullLogger.Instance, stop.Token);

        await PublishAsync([.. Enumerable.Range(1, 40).Select(i => new OrderCreated(i, i))]);
        await Until(() => Task.FromResult(a.Handled.Count + b.Handled.Count >= 40), TimeSpan.FromSeconds(20));

        var all = a.Handled.Concat(b.Handled).Select(h => h.OrderId).Order().ToList();
        Assert.Equal(Enumerable.Range(1, 40), all);
        Assert.NotEmpty(a.Handled);
        Assert.NotEmpty(b.Handled);
        await stop.CancelAsync();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcu_AnOrderRoundTripsWithItsAmount()
    {
        ProductionConditions.Require();
        var handler = new RecordingHandler();
        await using var channel = await _connection.CreateChannelAsync();
        using var stop = new CancellationTokenSource();
        await OrderConsumer.StartAsync(channel, _topology, handler, NullLogger.Instance, stop.Token);

        await PublishAsync(new OrderCreated(5, 1234.56m));
        await Until(() => Task.FromResult(!handler.Handled.IsEmpty), TimeSpan.FromSeconds(10));

        Assert.Equal((5, 1234.56m, "order-created-5"), handler.Handled.Single());
        await stop.CancelAsync();
    }

    private sealed class RecordingHandler(bool fails = false) : IOrderHandler
    {
        private int _attempts;
        public readonly ConcurrentQueue<(int OrderId, decimal Total, string MessageId)> Handled = new();
        public int Attempts => Volatile.Read(ref _attempts);

        public Task HandleAsync(OrderCreated order, string messageId, CancellationToken ct)
        {
            Interlocked.Increment(ref _attempts);
            if (fails)
                throw new InvalidOperationException("The billing system refused it.");
            Handled.Enqueue((order.OrderId, order.Total, messageId));
            return Task.CompletedTask;
        }
    }
}
