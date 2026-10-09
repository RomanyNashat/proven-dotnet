using Microsoft.AspNetCore.Http.HttpResults;

namespace Notification;

public static class NotificationEndpoints
{
    public static async Task<Results<Accepted<SendResponse>, ValidationProblem>> Send(
        SendRequest request, NotificationsDbContext db, NotificationSender sender, CancellationToken ct)
    {
        if (request.UserIds.Count is 0 or > 500)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["userIds"] = ["1 to 500 users"] });
        }

        var notification = new NotificationRecord(request.Template, request.UserIds);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        await sender.SendAsync(notification, ct);   // one attempt; a failure is recorded as Failed
        return TypedResults.Accepted($"/api/notifications/{notification.Id}/status", new SendResponse(notification.Id));
    }

    public static async Task<Results<Ok<StatusResponse>, NotFound>> GetStatus(int id, NotificationsDbContext db, CancellationToken ct)
    {
        var n = await db.Notifications.FindAsync([id], ct);
        return n is null ? TypedResults.NotFound() : TypedResults.Ok(new StatusResponse(n.Id, n.Status.ToString(), n.SentAt));
    }
}
