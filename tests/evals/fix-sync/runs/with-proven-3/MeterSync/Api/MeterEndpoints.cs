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
    private const int NationalIdLength = 10;

    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var meters = app.MapGroup("/meters");
        meters.MapPost("/", LinkAsync).RequireRateLimiting(RateLimits.LinkMeter);
        meters.MapGet("/", ListAsync);
        app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();
        return app;
    }

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, UnauthorizedHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var callerId))
        {
            return TypedResults.Unauthorized();
        }

        if (Validate(request) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The caller links a meter to themselves only. The national ID in the body has to be the caller's
        // own; anyone else's is NotFound, the same as an ID nobody has, so the answer tells nothing.
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == callerId && p.NationalId == request.NationalId, ct);
        if (patient is null)
        {
            return TypedResults.NotFound();
        }

        var serialKey = request.SerialNumber.ToUpperInvariant();
        if (await db.Meters.AnyAsync(m => EF.Property<string>(m, SyncDbContext.SerialKey) == serialKey, ct))
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
        CurrentPatient.TryGetId(user, out var patientId)
            ? TypedResults.Ok(await directory.MetersOfAsync(patientId, ct))
            : TypedResults.Unauthorized();

    private static Dictionary<string, string[]>? Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.NationalId is not { Length: NationalIdLength } nationalId || !nationalId.All(char.IsAsciiDigit))
        {
            errors[nameof(request.NationalId)] = ["A national ID of 10 digits is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SerialNumber) || request.SerialNumber.Length > SerialNumberMaxLength)
        {
            errors[nameof(request.SerialNumber)] = ["A serial number of up to 40 characters is required."];
        }

        return errors.Count == 0 ? null : errors;
    }
}
