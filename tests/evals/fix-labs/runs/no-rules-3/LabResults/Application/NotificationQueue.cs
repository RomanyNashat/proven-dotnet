using System.Threading.Channels;
using LabResults.Infrastructure;

namespace LabResults.Application;

public sealed record Notification(string To, string Text);

public sealed class NotificationQueue
{
    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(1000);

    public ChannelReader<Notification> Reader => _channel.Reader;

    /// <summary>False when the queue is full; a request must not block on the SMS backlog.</summary>
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
                // Includes gateway timeouts: one bad message must not kill the worker (and the host with it).
                logger.LogError(ex, "SMS failed");
            }
        }
    }
}
