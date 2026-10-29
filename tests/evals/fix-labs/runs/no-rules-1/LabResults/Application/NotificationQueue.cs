using System.Threading.Channels;
using LabResults.Infrastructure;

namespace LabResults.Application;

public sealed record Notification(string To, string Text);

public sealed class NotificationQueue
{
    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(1000);

    public ChannelReader<Notification> Reader => _channel.Reader;

    /// <summary>Never blocks the caller; returns false if the queue is full.</summary>
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
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "SMS failed");
            }
        }
    }
}
