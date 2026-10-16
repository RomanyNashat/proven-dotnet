using ClinicReminders.Application;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Api;

public static class ReminderEndpoints
{
    public static void MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reminders").RequireAuthorization();

        group.MapGet("/", (string? cursor, ICurrentUser user, ReminderQueries queries, CancellationToken ct) =>
            queries.PageAsync(user.Id, cursor, 20, ct));

        group.MapPost("/", async (ScheduleReminder request, ReminderService reminders, CancellationToken ct) =>
            TypedResults.Created($"/reminders/{await reminders.ScheduleAsync(request, ct)}"));
    }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public int Id => int.Parse(accessor.HttpContext!.User.FindFirst("sub")!.Value, System.Globalization.CultureInfo.InvariantCulture);
}
