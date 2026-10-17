using System.Text.RegularExpressions;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Api;

public static partial class ReminderEndpoints
{
    public static void MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reminders").RequireAuthorization();

        group.MapGet("/", async (string? cursor, ICurrentUser user, ReminderQueries queries, CancellationToken ct) =>
        {
            if (cursor is not null && !ReminderQueries.TryParseCursor(cursor, out _))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["Invalid cursor."] });
            }

            return Results.Ok(await queries.PageAsync(user.Id, cursor, 20, ct));
        });

        group.MapPost("/", async (ScheduleReminder request, ReminderService reminders, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (request.PatientId <= 0)
            {
                errors["patientId"] = ["patientId must be positive."];
            }

            if (request.Mobile is null || !MobilePattern().IsMatch(request.Mobile))
            {
                errors["mobile"] = ["mobile must be 7-15 digits, optionally prefixed with +."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            return TypedResults.Created($"/reminders/{await reminders.ScheduleAsync(request, ct)}");
        });
    }

    [GeneratedRegex(@"^\+?[0-9]{7,15}$")]
    private static partial Regex MobilePattern();
}

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public int Id
    {
        get
        {
            // The JWT handler may or may not have mapped "sub" to NameIdentifier.
            var user = accessor.HttpContext!.User;
            var sub = user.FindFirst("sub") ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            return int.TryParse(sub?.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)
                ? id
                : throw new UnauthorizedAccessException("Token has no numeric subject.");
        }
    }
}
