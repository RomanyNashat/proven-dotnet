using System.Threading.Channels;
using LabResults.Infrastructure;

namespace LabResults.Application;

public sealed record Notification(string To, string Text);

public sealed class NotificationQueue
{
    public const int Capacity = 1000;

    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(
        new BoundedChannelOptions(Capacity) { SingleReader = true });

    public ChannelReader<Notification> Reader => _channel.Reader;

    /// <summary>Queues an SMS without waiting; false when the queue is full.</summary>
    /// <remarks>Never blocks: the result is already saved, and a request stuck behind a slow gateway would be retried
    /// by the caller and record the result twice.</remarks>
    public bool TryEnqueue(Notification notification) => _channel.Writer.TryWrite(notification);
}

/// <summary>Sends the queued SMS one at a time, so a burst of results doesn't flood the gateway.</summary>
public sealed class NotificationWorker(NotificationQueue queue, IServiceScopeFactory scopes, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SmsClient>().SendAsync(notification.To, notification.Text, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Any failure (a timeout is a TaskCanceledException) must not end the loop: an unhandled exception
                // here stops the whole host. The phone number stays out of the log.
                logger.LogError(ex, "SMS failed");
            }
        }
    }
}
