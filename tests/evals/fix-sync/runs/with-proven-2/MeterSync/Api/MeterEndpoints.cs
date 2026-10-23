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
    public const string LinkRateLimit = "link-meter";

    /// <summary>Links per patient per minute, per pod. A patient links a meter or two, ever.</summary>
    public const int LinkPermitsPerMinute = 30;

    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var meters = app.MapGroup("/meters");
        meters.MapPost("/", LinkAsync).RequireRateLimiting(LinkRateLimit);
        meters.MapGet("/", ListAsync);
        app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();
        return app;
    }

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, UnauthorizedHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var patientId))
        {
            return TypedResults.Unauthorized();
        }

        if (LinkMeterValidation.Errors(request) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // The caller links a meter to themselves. Another patient's national ID is a 404, the same as one that
        // doesn't exist, so the answer doesn't say which national IDs are registered.
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null || patient.NationalId != request.NationalId)
        {
            return TypedResults.NotFound();
        }

        var serialKey = Meter.KeyOf(request.SerialNumber);
        if (await db.Meters.AnyAsync(m => m.SerialKey == serialKey, ct))
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
            return TypedResults.Conflict();   // the same serial linked by another request a moment ago
        }

        return TypedResults.Created("/meters", new MeterResponse(meter.SerialNumber, meter.LinkedAt));
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, UnauthorizedHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var patientId))
        {
            return TypedResults.Unauthorized();
        }

        var meters = await directory.MetersOfAsync(patientId, ct);
        return TypedResults.Ok<IReadOnlyList<MeterResponse>>([.. meters.Select(m => new MeterResponse(m.SerialNumber, m.LinkedAt))]);
    }
}
