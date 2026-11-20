using System.Threading.Channels;
using LabResults.Infrastructure;

namespace LabResults.Application;

public sealed record Notification(string To, string Text)
{
    // Never print the phone number if this record ends up in a log.
    public override string ToString() => "Notification { To = ***, Text = *** }";
}

public sealed class NotificationQueue
{
    private readonly Channel<Notification> _channel = Channel.CreateBounded<Notification>(
        new BoundedChannelOptions(1000) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    public ChannelReader<Notification> Reader => _channel.Reader;

    /// <summary>Queues without waiting; returns false when the queue is full so a request never blocks on the SMS gateway.</summary>
    public bool TryEnqueue(Notification notification) => _channel.Writer.TryWrite(notification);
}

/// <summary>Sends the queued SMS one at a time, so a burst of results doesn't flood the gateway.</summary>
public sealed class NotificationWorker(NotificationQueue queue, IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var notification in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SendAsync(notification, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task SendAsync(Notification notification, CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SmsClient>().SendAsync(notification.To, notification.Text, stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Any exception escaping ExecuteAsync stops the whole host, so every failure is handled here.
                // The phone number is not logged.
                if (attempt >= MaxAttempts)
                {
                    logger.LogError(ex, "SMS failed after {Attempts} attempts; dropped", attempt);
                    return;
                }

                logger.LogWarning(ex, "SMS attempt {Attempt} failed; retrying", attempt);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), time, stoppingToken);
            }
        }
    }
}
