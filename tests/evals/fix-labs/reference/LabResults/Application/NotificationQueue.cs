using System.Threading.Channels;
using LabResults.Infrastructure;

namespace LabResults.Application;

public sealed record Notification(string To, string Text);

public sealed class NotificationQueue
{
    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(1000);

    public ChannelReader<Notification> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(Notification notification, CancellationToken ct) => _channel.Writer.WriteAsync(notification, ct);

    public void Complete() => _channel.Writer.TryComplete();
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
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "SMS failed");
            }
        }
    }

    // On shutdown, send what's queued first: base.StopAsync cancels the loop at once and drops the rest.
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
