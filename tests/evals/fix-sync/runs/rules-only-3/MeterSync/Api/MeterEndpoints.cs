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
    private const int SerialNumberMaxLength = 40;
    private const int NationalIdMaxLength = 10;

    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var meters = app.MapGroup("/meters");
        meters.MapPost("/", LinkAsync);
        meters.MapGet("/", ListAsync);
        app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();
        return app;
    }

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, ForbidHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var patientId))
        {
            return TypedResults.Forbid();
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The caller links a meter to themselves only. Matching the national id against the caller's own
        // record (not any patient's) stops one patient from attaching a meter to another patient's account,
        // and the same 404 either way stops the endpoint from telling which national ids are registered.
        var nationalId = request.NationalId.Trim();
        var patient = await db.Patients.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == patientId && p.NationalId == nationalId, ct);
        if (patient is null)
        {
            return TypedResults.NotFound();
        }

        var serialNumber = request.SerialNumber.Trim();
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
            // Two requests for the same meter at once (on two pods): the index decides, the loser gets 409.
            return TypedResults.Conflict();
        }

        return TypedResults.Created("/meters", new MeterResponse(meter.SerialNumber, meter.LinkedAt));
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, ForbidHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct) =>
        CurrentPatient.TryGetId(user, out var patientId)
            ? TypedResults.Ok(await directory.MetersOfAsync(patientId, ct))
            : TypedResults.Forbid();

    private static Dictionary<string, string[]> Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.SerialNumber) || request.SerialNumber.Trim().Length > SerialNumberMaxLength)
        {
            errors[nameof(request.SerialNumber)] = [$"A serial number of up to {SerialNumberMaxLength} characters is required."];
        }

        if (string.IsNullOrWhiteSpace(request.NationalId) || request.NationalId.Trim().Length > NationalIdMaxLength)
        {
            errors[nameof(request.NationalId)] = [$"A national id of up to {NationalIdMaxLength} characters is required."];
        }

        return errors;
    }
}
