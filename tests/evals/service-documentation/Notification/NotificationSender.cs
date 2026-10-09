namespace Notification;

public sealed class NotificationSender(HttpClient http, NotificationsDbContext db, TimeProvider time, ILogger<NotificationSender> logger)
{
    public async Task SendAsync(NotificationRecord notification, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("v1/send", new { notification.Template, notification.UserIds }, ct);
        if (response.IsSuccessStatusCode)
        {
            notification.MarkSent(time.GetUtcNow());
        }
        else
        {
            logger.LogWarning("Push provider returned {Status} for notification {Id}", (int)response.StatusCode, notification.Id);
            notification.MarkFailed();
        }

        await db.SaveChangesAsync(ct);
    }
}
