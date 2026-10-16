using System.Globalization;
using System.Security.Claims;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClinicReminders.Api;

public static class ReminderEndpoints
{
    private const int PageSize = 20;

    public static void MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        // A caller without a numeric subject gets 403 here instead of a 500 from HttpCurrentUser.
        var group = app.MapGroup("/reminders").RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireAssertion(context => HttpCurrentUser.TryGetId(context.User, out _)));

        group.MapGet("/", async Task<Results<Ok<ReminderPage>, ValidationProblem>> (
            string? cursor, ICurrentUser user, ReminderQueries queries, CancellationToken ct) =>
        {
            if (!ReminderQueries.TryParseCursor(cursor, out var afterId))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["cursor"] = ["The cursor is not one this API returned."],
                });
            }

            return TypedResults.Ok(await queries.PageAsync(user.Id, afterId, PageSize, ct));
        });

        group.MapPost("/", async Task<Results<Created, ValidationProblem, ForbidHttpResult>> (
            ScheduleReminder request, ICurrentUser user, ReminderService reminders, TimeProvider time, CancellationToken ct) =>
        {
            // The caller is the patient (GET lists the caller's own reminders). Scheduling for another patient id
            // would let anyone send SMS about any patient to any number.
            if (request.PatientId != user.Id)
            {
                return TypedResults.Forbid();
            }

            var errors = ScheduleReminderRules.Validate(request, time.GetUtcNow());
            if (errors.Count > 0)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var id = await reminders.ScheduleAsync(request, ct);
            return TypedResults.Created($"/reminders/{id}");
        });
    }
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public int Id =>
        TryGetId(accessor.HttpContext?.User, out var id)
            ? id
            : throw new InvalidOperationException("The current request has no numeric subject claim.");

    /// <summary>Reads the caller's id from "sub" (or its mapped name, if a scheme maps inbound claims).</summary>
    /// <param name="user">The caller.</param>
    /// <param name="id">The id.</param>
    /// <returns>True when the caller has a positive integer subject.</returns>
    public static bool TryGetId(ClaimsPrincipal? user, out int id)
    {
        var value = user?.FindFirst("sub")?.Value ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    }
}
