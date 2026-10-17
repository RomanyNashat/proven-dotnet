using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClinicReminders.Api;

public static class ReminderEndpoints
{
    private const int PageSize = 20;

    public static void MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reminders").RequireAuthorization();

        group.MapGet("/", async Task<Results<Ok<ReminderPage>, ValidationProblem>> (
            string? cursor, ICurrentUser user, ReminderQueries queries, CancellationToken ct) =>
        {
            ReminderCursor? after = null;
            if (!string.IsNullOrEmpty(cursor))
            {
                if (!ReminderCursor.TryDecode(cursor, out var decoded))
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [nameof(cursor)] = ["Not a cursor from this API. Pass back the 'next' value of the previous page."],
                    });
                }

                after = decoded;
            }

            return TypedResults.Ok(await queries.PageAsync(user.Id, after, PageSize, ct));
        });

        group.MapPost("/", async Task<Results<Created, ValidationProblem, ForbidHttpResult>> (
            ScheduleReminder request, ReminderService reminders, CancellationToken ct) =>
            await reminders.ScheduleAsync(request, ct) switch
            {
                ScheduleResult.Scheduled s => TypedResults.Created($"/reminders/{s.Id}"),
                ScheduleResult.Invalid i => TypedResults.ValidationProblem(i.Errors),
                _ => TypedResults.Forbid(),
            });
    }
}
