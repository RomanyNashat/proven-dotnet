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

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, ForbidHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var callerId))
        {
            return TypedResults.Forbid();
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // A patient links only their own meter. Someone else's national id gets the same answer as an
        // unknown one, so the endpoint can't be used to find out which national ids are registered.
        var isCaller = await db.Patients.AnyAsync(p => p.Id == callerId && p.NationalId == request.NationalId, ct);
        if (!isCaller)
        {
            return TypedResults.NotFound();
        }

        if (await db.Meters.AnyAsync(m => m.SerialNumber == request.SerialNumber, ct))
        {
            return TypedResults.Conflict();
        }

        var meter = new Meter(callerId, request.SerialNumber, time.GetUtcNow());
        db.Meters.Add(meter);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two requests linked the same serial at once; the unique index let one through.
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
}
