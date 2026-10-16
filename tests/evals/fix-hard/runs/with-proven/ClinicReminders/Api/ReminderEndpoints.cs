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

        group.MapGet("/", List);
        group.MapGet("/{id:int}", GetById);
        group.MapPost("/", Schedule);
    }

    private static async Task<Results<Ok<ReminderPage>, ValidationProblem, UnauthorizedHttpResult>> List(
        string? cursor, ICurrentUser user, ReminderQueries queries, CancellationToken ct)
    {
        if (user.Id is not { } patientId)
        {
            return TypedResults.Unauthorized();
        }

        // No cursor, or an empty one (?cursor=), is the first page.
        ReminderCursor? after = null;
        if (!string.IsNullOrWhiteSpace(cursor) && !ReminderCursor.TryParse(cursor, out after))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["The cursor is not valid."] });
        }

        return TypedResults.Ok(await queries.PageAsync(patientId, after, PageSize, ct));
    }

    // Ids are sequential ints: the query filters by the caller, and someone else's reminder is a 404.
    private static async Task<Results<Ok<ReminderRow>, NotFound, UnauthorizedHttpResult>> GetById(
        int id, ICurrentUser user, ReminderQueries queries, CancellationToken ct)
    {
        if (user.Id is not { } patientId)
        {
            return TypedResults.Unauthorized();
        }

        var row = await queries.FindAsync(patientId, id, ct);
        if (row is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(row);
    }

    // The caller is the patient (GET lists the caller's own reminders), so a reminder for another patient,
    // and an SMS to a number of the caller's choosing in that patient's name, is refused.
    private static async Task<Results<Created, ValidationProblem, UnauthorizedHttpResult, ForbidHttpResult>> Schedule(
        ScheduleReminder request, ICurrentUser user, ReminderService reminders, TimeProvider time, CancellationToken ct)
    {
        if (user.Id is not { } callerId)
        {
            return TypedResults.Unauthorized();
        }

        var errors = ScheduleReminderValidator.Validate(request, time.GetUtcNow());
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (request.PatientId != callerId)
        {
            return TypedResults.Forbid();
        }

        var id = await reminders.ScheduleAsync(callerId, request, ct);
        return TypedResults.Created($"/reminders/{id}");
    }
}
