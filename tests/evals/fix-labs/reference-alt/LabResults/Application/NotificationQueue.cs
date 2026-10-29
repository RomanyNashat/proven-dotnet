using LabResults.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Application;

/// <summary>Sends the saved SMS one at a time, every second, so a burst of results doesn't flood the gateway.</summary>
public sealed class NotificationWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SendPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "SMS run failed");
            }
        }
    }

    private async Task SendPendingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LabDbContext>();
        var sms = scope.ServiceProvider.GetRequiredService<SmsClient>();
        foreach (var pending in await db.Sms.Where(s => s.SentAt == null).OrderBy(s => s.Id).Take(50).ToListAsync(ct))
        {
            await sms.SendAsync(pending.To, pending.Text, ct);
            pending.MarkSent(time.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
    }
}
