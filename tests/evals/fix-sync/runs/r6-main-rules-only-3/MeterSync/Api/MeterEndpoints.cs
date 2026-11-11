using System.Security.Claims;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Api;

public sealed record LinkMeterRequest(string? NationalId, string? SerialNumber);

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

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var nationalId = request.NationalId!.Trim();
        var serialNumber = request.SerialNumber!.Trim();

        // A patient links meters to themselves only. Someone else's national id gets the same 404 as an
        // unknown one, so the endpoint can't be used to find out who is a patient.
        var patient = await db.Patients.AsNoTracking()
            .SingleOrDefaultAsync(p => p.NationalId == nationalId && p.Id == patientId, ct);
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
            // Linked by a concurrent request (possibly on another pod) between the check and the insert.
            return TypedResults.Conflict();
        }

        return TypedResults.Created("/meters", new MeterResponse(meter.SerialNumber, meter.LinkedAt));
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, UnauthorizedHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct)
    {
        if (CurrentPatient.Id(user) is not { } patientId)
        {
            return TypedResults.Unauthorized();
        }

        var meters = await directory.MetersOfAsync(patientId, ct);
        return TypedResults.Ok<IReadOnlyList<MeterResponse>>(
            meters.Select(m => new MeterResponse(m.SerialNumber, m.LinkedAt)).ToList());
    }

    private static Dictionary<string, string[]> Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.NationalId) || request.NationalId.Trim().Length > 10)
        {
            errors[nameof(request.NationalId)] = ["A national id of up to 10 characters is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SerialNumber) || request.SerialNumber.Trim().Length > Meter.SerialNumberMaxLength)
        {
            errors[nameof(request.SerialNumber)] = [$"A serial number of up to {Meter.SerialNumberMaxLength} characters is required."];
        }

        return errors;
    }
}
