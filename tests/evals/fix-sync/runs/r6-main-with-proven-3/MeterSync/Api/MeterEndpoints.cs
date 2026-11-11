using System.Security.Claims;
using MeterSync.Application;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Api;

public static class MeterEndpoints
{
    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var meters = app.MapGroup("/meters");
        meters.MapPost("/", LinkAsync);
        meters.MapGet("/", ListAsync);
        app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();
        return app;
    }

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, UnauthorizedHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (CurrentPatient.TryGetId(user) is not { } patientId)
        {
            return TypedResults.Unauthorized();
        }

        if (request.Validate() is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The caller may only link a meter to themselves. A national id that isn't theirs gets the same
        // 404 as one that doesn't exist, so the endpoint can't be used to find out who is registered.
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null || !string.Equals(patient.NationalId, request.NationalId, StringComparison.Ordinal))
        {
            return TypedResults.NotFound();
        }

        if (await db.Meters.AnyAsync(m => m.SerialNumber == request.SerialNumber, ct))
        {
            return TypedResults.Conflict();
        }

        var meter = new Meter(patient.Id, request.SerialNumber, time.GetUtcNow());
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request linked the same serial between the check above and this insert.
            return TypedResults.Conflict();
        }

        return TypedResults.Created("/meters", new MeterResponse(meter.SerialNumber, meter.LinkedAt));
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, UnauthorizedHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct) =>
        CurrentPatient.TryGetId(user) is { } patientId
            ? TypedResults.Ok(await directory.MetersOfAsync(patientId, ct))
            : TypedResults.Unauthorized();
}
