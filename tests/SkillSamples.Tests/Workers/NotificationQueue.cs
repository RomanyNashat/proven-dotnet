using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkillSamples.Workers;

public sealed record NotificationMessage(int Id, string Text);

public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken ct);
}

// In-process only: anything still queued is lost if the pod is killed. A message that must not be lost
// goes through the outbox and Kafka instead.
public sealed class NotificationQueue
{
    private readonly Channel<NotificationMessage> _channel = Channel.CreateBounded<NotificationMessage>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.Wait });   // backpressure when full

    public ChannelReader<NotificationMessage> Reader => _channel.Reader;

    // Throws ChannelClosedException once shutdown has started; callers treat that as "try later".
    public ValueTask EnqueueAsync(NotificationMessage message, CancellationToken ct) => _channel.Writer.WriteAsync(message, ct);

    public void Complete() => _channel.Writer.TryComplete();
}

public sealed class NotificationDispatchWorker(
    NotificationQueue queue, IServiceScopeFactory scopeFactory, ILogger<NotificationDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Ends by itself when the queue is completed and empty (see StopAsync).
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<INotificationSender>().SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to dispatch notification {Id}", message.Id);
            }
        }
    }

    // Drain before cancelling. BackgroundService.StopAsync cancels stoppingToken straight away, so calling
    // it first drops whatever is still queued. Instead: refuse new items, wait for the queue to empty (up
    // to the host's shutdown timeout, which is what cancellationToken carries), then cancel the rest.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        queue.Complete();
        if (ExecuteTask is { } running)
        {
            await Task.WhenAny(running, Task.Delay(Timeout.Infinite, cancellationToken));
        }

        await base.StopAsync(cancellationToken);
    }
}
