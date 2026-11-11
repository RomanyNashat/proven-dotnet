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

    private static async Task<Results<Created<MeterResponse>, UnauthorizedHttpResult, NotFound, Conflict, ValidationProblem>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var patientId))
        {
            return TypedResults.Unauthorized();
        }

        var errors = LinkMeterRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // A caller may only link a meter to themselves: the national id must be the caller's own.
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == patientId && p.NationalId == request.NationalId, ct);
        if (patient is null)
        {
            return TypedResults.NotFound();
        }

        var meter = new Meter(patient.Id, request.SerialNumber, time.GetUtcNow());
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return TypedResults.Conflict();
        }

        return TypedResults.Created("/meters", new MeterResponse(meter.SerialNumber, meter.LinkedAt));
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, UnauthorizedHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct) =>
        CurrentPatient.TryGetId(user, out var patientId)
            ? TypedResults.Ok(await directory.MetersOfAsync(patientId, ct))
            : TypedResults.Unauthorized();
}
