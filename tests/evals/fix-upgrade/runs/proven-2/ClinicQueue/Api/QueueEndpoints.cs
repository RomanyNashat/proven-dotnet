using System.Globalization;
using System.Security.Claims;
using ClinicQueue.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ClinicQueue.Api;

public sealed record BookVisit(int ClinicId, string Day);
public sealed record Booked(int Id, int Position);
public sealed record VisitDay(string Day);
public sealed record QueueView(int ClinicId, string Day, int[] Positions);
public sealed record Caller(string ClientIp);

public static class QueueEndpoints
{
    public static void MapQueueEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();

        app.MapGet("/clinics", (ClinicDirectory directory) => TypedResults.Ok(directory.All));

        app.MapGet("/clinics/{id:int}/queue", async Task<Results<Ok<QueueView>, BadRequest>> (
            int id, string day, QueueSnapshots snapshots, CancellationToken ct) =>
        {
            if (!TryParseDay(day, out var parsed))
            {
                return TypedResults.BadRequest();
            }

            var snapshot = await snapshots.GetAsync(id, parsed, ct);
            return TypedResults.Ok(new QueueView(snapshot.ClinicId, snapshot.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), snapshot.Positions));
        });

        app.MapPost("/visits", async Task<Results<Created<Booked>, BadRequest, ForbidHttpResult>> (
            BookVisit request, ClaimsPrincipal user, VisitBooking booking, CancellationToken ct) =>
        {
            if (!TryGetPatientId(user, out var patientId))
            {
                return TypedResults.Forbid();
            }

            if (!TryParseDay(request.Day, out var day))
            {
                return TypedResults.BadRequest();
            }

            var visit = await booking.BookAsync(request.ClinicId, patientId, day, ct);
            return visit is null
                ? TypedResults.BadRequest()
                : TypedResults.Created($"/visits/{visit.Id}", new Booked(visit.Id, visit.Position));
        });

        app.MapGet("/visits/{id:int}/day", async Task<Results<Ok<VisitDay>, NotFound, ForbidHttpResult>> (
            int id, ClaimsPrincipal user, VisitReads reads, CancellationToken ct) =>
        {
            if (!TryGetPatientId(user, out var patientId))
            {
                return TypedResults.Forbid();
            }

            var day = await reads.GetDayAsync(id, patientId, ct);
            return day is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(new VisitDay(day.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        });

        app.MapGet("/whoami", (HttpContext http) => TypedResults.Ok(new Caller(http.Connection.RemoteIpAddress?.ToString() ?? "")));
    }

    // A valid token whose sub isn't a patient id (a staff UUID, or none) is refused, not a 500.
    private static bool TryGetPatientId(ClaimsPrincipal user, out int patientId) =>
        int.TryParse(user.FindFirstValue("sub"), NumberStyles.None, CultureInfo.InvariantCulture, out patientId);

    private static bool TryParseDay(string text, out DateTime day) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
