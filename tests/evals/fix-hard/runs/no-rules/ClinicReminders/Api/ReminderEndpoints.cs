using System.Security.Claims;
using System.Text.RegularExpressions;
using ClinicReminders.Application;
using ClinicReminders.Infrastructure;

namespace ClinicReminders.Api;

public static partial class ReminderEndpoints
{
    public static void MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reminders").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (string? cursor, ICurrentUser user, ReminderQueries queries, CancellationToken ct) =>
        {
            var page = await queries.PageAsync(user.Id, cursor, 20, ct);
            return page is null
                ? Results.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["Invalid cursor."] })
                : Results.Ok(page);
        });

        group.MapPost("/", async Task<IResult> (ScheduleReminder request, ReminderService reminders, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (request.PatientId <= 0)
            {
                errors["patientId"] = ["patientId must be positive."];
            }

            if (string.IsNullOrWhiteSpace(request.Mobile) || !MobilePattern().IsMatch(request.Mobile))
            {
                errors["mobile"] = ["mobile must be 7-15 digits with an optional leading '+'."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var id = await reminders.ScheduleAsync(request, ct);
            return Results.Created($"/reminders/{id}", new { id });
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
            var user = accessor.HttpContext?.User;
            var sub = user?.FindFirst("sub")?.Value ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(sub, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id)
                ? id
                : throw new InvalidOperationException("Authenticated user has no numeric 'sub' claim.");
        }
    }
}
