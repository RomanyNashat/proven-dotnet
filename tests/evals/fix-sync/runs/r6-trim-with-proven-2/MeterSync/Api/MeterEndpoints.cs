using System.Security.Claims;
using System.Text.RegularExpressions;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MeterSync.Api;

public sealed record LinkMeterRequest(string NationalId, string SerialNumber);

public sealed record MeterResponse(string SerialNumber, DateTimeOffset LinkedAt);

public static partial class MeterEndpoints
{
    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var meters = app.MapGroup("/meters");
        meters.MapPost("/", LinkAsync);
        meters.MapGet("/", ListAsync);
        app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();
        return app;
    }

    // \z, not $: $ also matches before a trailing newline.
    [GeneratedRegex(@"^[0-9]{10}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,39}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SerialNumberPattern();

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, UnauthorizedHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, SyncDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var callerId))
        {
            return TypedResults.Unauthorized();
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Matching on the caller as well as the national id: a patient links meters to themselves only,
        // and "not yours" answers like "doesn't exist" so national ids can't be probed.
        var patient = await db.Patients
            .SingleOrDefaultAsync(p => p.Id == callerId && p.NationalId == request.NationalId, ct);
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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two link requests for the same serial raced past the AnyAsync check.
            return TypedResults.Conflict();
        }

        return TypedResults.Created("/meters", new MeterResponse(meter.SerialNumber, meter.LinkedAt));
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, UnauthorizedHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct) =>
        CurrentPatient.TryGetId(user, out var patientId)
            ? TypedResults.Ok(await directory.MetersOfAsync(patientId, ct))
            : TypedResults.Unauthorized();

    private static Dictionary<string, string[]> Validate(LinkMeterRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.NationalId is not { } nationalId || !NationalIdPattern().IsMatch(nationalId))
        {
            errors[nameof(request.NationalId)] = ["A national id of exactly 10 digits is required."];
        }

        if (request.SerialNumber is not { } serial || !SerialNumberPattern().IsMatch(serial))
        {
            errors[nameof(request.SerialNumber)] = ["A serial number of up to 40 letters, digits, '.', '_' or '-' is required."];
        }

        return errors;
    }
}
