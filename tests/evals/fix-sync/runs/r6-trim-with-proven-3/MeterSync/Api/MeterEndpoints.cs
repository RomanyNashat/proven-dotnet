using System.Security.Claims;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

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

        var errors = LinkMeterRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The caller links a meter to themselves only. A national id that isn't theirs gets the same
        // answer as an unknown one, so the endpoint can't be used to find out whose ids exist.
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null || !string.Equals(patient.NationalId, request.NationalId, StringComparison.Ordinal))
        {
            return TypedResults.NotFound();
        }

        var serial = request.SerialNumber.Trim();
        if (await db.Meters.AnyAsync(m => m.SerialNumber == serial, ct))
        {
            return TypedResults.Conflict();
        }

        var meter = new Meter(patient.Id, serial, time.GetUtcNow());
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresErrors.IsUniqueViolation(ex))
        {
            // Another request linked the same serial between our check and our insert.
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
