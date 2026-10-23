using System.Security.Claims;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Api;

public sealed record LinkMeterRequest(string NationalId, string SerialNumber);

public sealed record MeterResponse(string SerialNumber, DateTimeOffset LinkedAt);

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
        if (CurrentPatient.Id(user) is not { } patientId)
        {
            return TypedResults.Unauthorized();
        }

        var serialNumber = request.SerialNumber?.Trim();
        if (string.IsNullOrEmpty(serialNumber) || serialNumber.Length > 40)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.SerialNumber)] = ["A serial number of up to 40 characters is required."],
            });
        }

        // A patient can only link a meter to themselves: the national id must be their own.
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == patientId && p.NationalId == request.NationalId, ct);
        if (patient is null)
        {
            return TypedResults.NotFound();
        }

        if (await db.Meters.AnyAsync(m => m.SerialNumber == serialNumber, ct))
        {
            return TypedResults.Conflict();
        }

        var meter = new Meter(patient.Id, serialNumber, time.GetUtcNow());
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost a race with another request (or pod) linking the same serial number.
            return TypedResults.Conflict();
        }

        return TypedResults.Created("/meters", new MeterResponse(meter.SerialNumber, meter.LinkedAt));
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, UnauthorizedHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct) =>
        CurrentPatient.Id(user) is { } patientId
            ? TypedResults.Ok(await directory.MetersOfAsync(patientId, ct))
            : TypedResults.Unauthorized();
}
