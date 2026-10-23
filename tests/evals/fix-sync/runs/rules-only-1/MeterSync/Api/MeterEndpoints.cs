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
    private const int NationalIdMaxLength = 10;
    private const int SerialNumberMaxLength = 40;

    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var meters = app.MapGroup("/meters");
        meters.MapPost("/", LinkAsync);
        meters.MapGet("/", ListAsync);
        app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();
        return app;
    }

    /// <summary>Validates the shape of a link request.</summary>
    /// <param name="request">The request body.</param>
    /// <returns>The errors by field; empty when the request is valid.</returns>
    public static Dictionary<string, string[]> Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.NationalId) || request.NationalId.Length > NationalIdMaxLength)
        {
            errors[nameof(request.NationalId)] = [$"A national id of up to {NationalIdMaxLength} characters is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SerialNumber) || request.SerialNumber.Length > SerialNumberMaxLength)
        {
            errors[nameof(request.SerialNumber)] = [$"A serial number of up to {SerialNumberMaxLength} characters is required."];
        }

        return errors;
    }

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, ForbidHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!CurrentPatient.TryGetId(user, out var patientId))
        {
            return TypedResults.Forbid();
        }

        // A patient links meters to themselves only. Another patient's national id gets the same answer as
        // an unknown one, so the endpoint can't be used to find out which national ids are registered.
        var patient = await db.Patients.AsNoTracking()
            .SingleOrDefaultAsync(p => p.NationalId == request.NationalId && p.Id == patientId, ct);
        if (patient is null)
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
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Another request (or pod) linked the same serial number between the check and the insert.
            return TypedResults.Conflict();
        }

        return TypedResults.Created("/meters", new MeterResponse(meter.SerialNumber, meter.LinkedAt));
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, ForbidHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var patientId))
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(await directory.MetersOfAsync(patientId, ct));
    }
}
