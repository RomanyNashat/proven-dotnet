using System.Threading.Channels;
using LabResults.Infrastructure;

namespace LabResults.Application;

public sealed record Notification(string To, string Text);

public sealed class NotificationQueue
{
    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(
        new BoundedChannelOptions(1000) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public ChannelReader<Notification> Reader => _channel.Reader;

    /// <summary>Never blocks the caller: returns false if the queue is full.</summary>
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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Any failure (HTTP error, timeout, bad response) must not kill the worker: an unhandled exception in a
                // BackgroundService stops the whole host. The phone number is deliberately not logged.
                logger.LogError(ex, "SMS notification failed");
            }
        }
    }
}
