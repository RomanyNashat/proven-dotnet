using LabResults.Infrastructure;

namespace LabResults.Application;

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
                // Any escape here (an HttpClient timeout is a TaskCanceledException, not an
                // HttpRequestException) would end the worker and, by default, stop the whole host.
                logger.LogError(ex, "SMS failed");
            }
        }
    }
}
