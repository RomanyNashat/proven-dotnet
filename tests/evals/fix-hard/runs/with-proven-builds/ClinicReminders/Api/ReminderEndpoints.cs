using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClinicReminders.Api;

public static class ReminderEndpoints
{
    private const int PageSize = 20;

    public static void MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reminders").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:int}", GetAsync);
        group.MapPost("/", ScheduleAsync).RequireRateLimiting(RateLimits.ScheduleReminder);
    }

    private static async Task<Results<Ok<ReminderPage>, ValidationProblem, ForbidHttpResult>> ListAsync(
        string? cursor, ICurrentUser user, ReminderQueries queries, CancellationToken ct)
    {
        if (!user.TryGetPatientId(out var patientId))
        {
            return TypedResults.Forbid();
        }

        if (!ReminderCursor.TryParse(cursor, out var after))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["The cursor is not one this API returned."] });
        }

        return TypedResults.Ok(await queries.PageAsync(patientId, after, PageSize, ct));
    }

    // Someone else's reminder is NotFound, like one that doesn't exist: ids are sequential ints.
    private static async Task<Results<Ok<ReminderRow>, NotFound, ForbidHttpResult>> GetAsync(
        int id, ICurrentUser user, ReminderQueries queries, CancellationToken ct)
    {
        if (!user.TryGetPatientId(out var patientId))
        {
            return TypedResults.Forbid();
        }

        return await queries.FindAsync(patientId, id, ct) is { } row ? TypedResults.Ok(row) : TypedResults.NotFound();
    }

    private static async Task<Results<Created<ScheduledReminder>, ValidationProblem, ForbidHttpResult>> ScheduleAsync(
        ScheduleReminder request, ICurrentUser user, IValidator<ScheduleReminder> validator, ReminderService reminders, CancellationToken ct)
    {
        // The patient is the caller. A body naming someone else is refused, not quietly rewritten.
        if (!user.TryGetPatientId(out var patientId) || (request.PatientId is { } named && named != patientId))
        {
            return TypedResults.Forbid();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        var id = await reminders.ScheduleAsync(patientId, request, ct);
        return TypedResults.Created($"/reminders/{id}", new ScheduledReminder(id));
    }
}
