using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;
using NATS.Net;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.Nats;

public sealed class NatsTests : IAsyncLifetime
{
    private static readonly string Url = Environment.GetEnvironmentVariable("NATS_URL") ?? "nats://localhost:4222";
    private readonly string _run = Guid.NewGuid().ToString("N")[..8];   // keeps parallel test runs apart
    private readonly NatsClient _nats = new(Url);

    public async Task InitializeAsync() => await _nats.ConnectAsync();

    public async Task DisposeAsync() => await _nats.DisposeAsync();

    private string Subject(string name) => $"{_run}.{name}";

    // SubscribeCoreAsync returns once the server has the subscription, so nothing published after it is missed.
    private async Task<(INatsSub<T> Sub, ConcurrentQueue<T> Got, Task Reading)> SubscribeAsync<T>(string subject, string? queueGroup = null)
    {
        var sub = await _nats.Connection.SubscribeCoreAsync<T>(subject, queueGroup);
        var got = new ConcurrentQueue<T>();
        var reading = Task.Run(async () =>
        {
            await foreach (var msg in sub.Msgs.ReadAllAsync())
                got.Enqueue(msg.Data!);
        });
        return (sub, got, reading);
    }

    private static async Task Settle(Func<bool> done)
    {
        for (var i = 0; i < 50 && !done(); i++)
            await Task.Delay(100);
        await Task.Delay(300);   // and a moment for anything extra to arrive
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_ThreeInstancesShareAQueueGroup_EachOrderIsHandledOnce()
    {
        var subject = Subject("orders.created");
        var workers = new[] { await SubscribeAsync<OrderCreated>(subject, "order-workers"), await SubscribeAsync<OrderCreated>(subject, "order-workers"), await SubscribeAsync<OrderCreated>(subject, "order-workers") };

        for (var id = 1; id <= 30; id++)
            await _nats.PublishAsync(subject, new OrderCreated(id));
        await Settle(() => workers.Sum(w => w.Got.Count) >= 30);

        var handled = workers.SelectMany(w => w.Got).Select(o => o.OrderId).ToList();
        Assert.Equal(Enumerable.Range(1, 30), handled.Order());          // every order, once
        Assert.True(workers.Count(w => !w.Got.IsEmpty) > 1);             // and the work was shared
        foreach (var w in workers) await w.Sub.DisposeAsync();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TwoServicesSubscribeWithoutAQueueGroup_BothGetEveryEvent()
    {
        var subject = Subject("orders.created");
        var (billing, toBilling, _) = await SubscribeAsync<OrderCreated>(subject);
        var (shipping, toShipping, _) = await SubscribeAsync<OrderCreated>(subject);

        await _nats.PublishAsync(subject, new OrderCreated(1));
        await Settle(() => toBilling.Count + toShipping.Count >= 2);

        Assert.Single(toBilling);
        Assert.Single(toShipping);
        await billing.DisposeAsync();
        await shipping.DisposeAsync();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_NoOneIsSubscribedWhenTheEventIsPublished_ItIsGoneForGood()
    {
        var subject = Subject("orders.created");
        await _nats.PublishAsync(subject, new OrderCreated(1));   // nobody listening: at most once

        var (sub, got, _) = await SubscribeAsync<OrderCreated>(subject);
        await _nats.PublishAsync(subject, new OrderCreated(2));
        await Settle(() => got.Count >= 1);

        Assert.Equal(new[] { 2 }, got.Select(o => o.OrderId));
        await sub.DisposeAsync();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_TheServiceUsesABareConnection_PublishingARecordThrows()
    {
        await using var bare = new NatsConnection(new NatsOpts { Url = Url });

        await Assert.ThrowsAsync<NatsException>(async () => await bare.PublishAsync(Subject("orders.created"), new OrderCreated(1)));
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AWorkerIsRunning_ItHandlesThePublishedOrder()
    {
        var handled = new TaskCompletionSource<OrderCreated>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection().AddNats(Url, "orders-worker")
            .AddSingleton<IOrderCreatedHandler>(new RecordingHandler(handled)).BuildServiceProvider();
        var worker = new OrderCreatedWorker(services.GetRequiredService<INatsClient>(), services.GetRequiredService<IOrderCreatedHandler>());
        await worker.StartAsync(CancellationToken.None);
        try
        {
            // The worker's subscription starts in the background; publish until it's listening.
            for (var i = 0; i < 50 && !handled.Task.IsCompleted; i++)
            {
                await _nats.PublishAsync(OrderCreatedWorker.Subject, new OrderCreated(99));
                await Task.WhenAny(handled.Task, Task.Delay(100));
            }
            Assert.Equal(99, (await handled.Task.WaitAsync(TimeSpan.FromSeconds(5))).OrderId);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
            await services.DisposeAsync();
        }
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public async Task Story_AServiceAsksForAPatient_OneResponderAnswers_AndNoResponderFailsFast()
    {
        using var stop = new CancellationTokenSource();
        var responder = PatientLookup.ServeAsync(_nats, id => new PatientSummary(id, "Sara"), stop.Token);

        PatientSummary? answer = null;
        for (var i = 0; i < 50 && answer is null; i++)
        {
            try { answer = await PatientLookup.AskAsync(_nats, 42, CancellationToken.None); }
            catch (NatsNoRespondersException) { await Task.Delay(100); }   // the responder isn't subscribed yet
        }
        Assert.Equal(new PatientSummary(42, "Sara"), answer);

        await stop.CancelAsync();
        await Task.WhenAny(responder, Task.Delay(2000));
        await Assert.ThrowsAsync<NatsNoRespondersException>(async () =>
            await _nats.RequestAsync<LookupPatient, PatientSummary>(Subject("nobody.home"), new LookupPatient(1)));
    }

    [Theory]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    [InlineData("patient.*.updated", "patient.42.updated", true)]
    [InlineData("patient.*.updated", "patient.42.notes.updated", false)]
    [InlineData("orders.>", "orders.eu.shipped", true)]
    [InlineData("orders.>", "orders", false)]
    public async Task Story_WildcardSubscriptions_MatchWhatTheSkillSays(string pattern, string subject, bool matches)
    {
        var (sub, got, _) = await SubscribeAsync<string>(Subject(pattern));

        await _nats.PublishAsync(Subject(subject), "hello");
        await Settle(() => !got.IsEmpty);

        Assert.Equal(matches, !got.IsEmpty);
        await sub.DisposeAsync();
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public async Task Production_NoIcu_ArabicTextTravelsIntact()
    {
        ProductionConditions.Require();
        var subject = Subject("patient.lookup");
        using var stop = new CancellationTokenSource();
        var responder = Task.Run(async () =>
        {
            await foreach (var request in _nats.SubscribeAsync<LookupPatient>(subject, cancellationToken: stop.Token))
                await request.ReplyAsync(new PatientSummary(request.Data!.PatientId, "سارة أحمد"));
        });

        PatientSummary? answer = null;
        for (var i = 0; i < 50 && answer is null; i++)
        {
            try { answer = (await _nats.RequestAsync<LookupPatient, PatientSummary>(subject, new LookupPatient(7))).Data; }
            catch (NatsNoRespondersException) { await Task.Delay(100); }
        }

        Assert.Equal(new PatientSummary(7, "سارة أحمد"), answer);
        await stop.CancelAsync();
    }

    private sealed class RecordingHandler(TaskCompletionSource<OrderCreated> handled) : IOrderCreatedHandler
    {
        public Task HandleAsync(OrderCreated order, CancellationToken ct)
        {
            handled.TrySetResult(order);
            return Task.CompletedTask;
        }
    }
}
