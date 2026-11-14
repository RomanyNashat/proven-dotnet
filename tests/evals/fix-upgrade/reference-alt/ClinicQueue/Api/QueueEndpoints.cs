using System.Globalization;
using System.Security.Claims;
using ClinicQueue.Application;
using ClinicQueue.Domain;
using ClinicQueue.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

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

        app.MapPost("/visits", async Task<Results<Created<Booked>, BadRequest>> (
            BookVisit request, ClaimsPrincipal user, QueueDbContext db, CancellationToken ct) =>
        {
            if (!TryParseDay(request.Day, out var day) || !await db.Clinics.AnyAsync(c => c.Id == request.ClinicId, ct))
            {
                return TypedResults.BadRequest();
            }

            var position = await db.Visits.CountAsync(v => v.ClinicId == request.ClinicId && v.Day == day, ct) + 1;
            var visit = new Visit(request.ClinicId, PatientId(user), day, position);
            db.Visits.Add(visit);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/visits/{visit.Id}", new Booked(visit.Id, position));
        });

        app.MapGet("/visits/{id:int}/day", async Task<Results<Ok<VisitDay>, NotFound>> (
            int id, ClaimsPrincipal user, VisitReads reads, CancellationToken ct) =>
        {
            var day = await reads.GetDayAsync(id, PatientId(user), ct);
            return day is null
                ? TypedResults.NotFound()
                : TypedResults.Ok(new VisitDay(day.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        });

        app.MapGet("/whoami", (HttpContext http) => TypedResults.Ok(new Caller(http.Connection.RemoteIpAddress?.ToString() ?? "")));
    }

    private static int PatientId(ClaimsPrincipal user) => int.Parse(user.FindFirstValue("sub")!, CultureInfo.InvariantCulture);

    private static bool TryParseDay(string text, out DateOnly day) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
