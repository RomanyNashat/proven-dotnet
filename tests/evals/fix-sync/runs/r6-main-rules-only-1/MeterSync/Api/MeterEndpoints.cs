using System.Security.Claims;
using MeterSync.Application;
using MeterSync.Domain;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MeterSync.Api;

public sealed record LinkMeterRequest(string NationalId, string SerialNumber);

public sealed record MeterResponse(string SerialNumber, DateTimeOffset LinkedAt);

public static class MeterEndpoints
{
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
        LinkMeterRequest request, ClaimsPrincipal user, MeterLinker linker, CancellationToken ct)
    {
        if (CurrentPatient.Id(user) is not { } callerId)
        {
            return TypedResults.Forbid();
        }

        var serialNumber = request.SerialNumber?.Trim();
        var nationalId = request.NationalId?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(serialNumber) || serialNumber.Length > Meter.SerialNumberMaxLength)
        {
            errors[nameof(request.SerialNumber)] = [$"A serial number of up to {Meter.SerialNumberMaxLength} characters is required."];
        }

        if (string.IsNullOrWhiteSpace(nationalId) || nationalId.Length > NationalIdMaxLength)
        {
            errors[nameof(request.NationalId)] = [$"A national id of up to {NationalIdMaxLength} characters is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (outcome, meter) = await linker.LinkAsync(callerId, nationalId!, serialNumber!, ct);
        return outcome switch
        {
            LinkOutcome.Linked => TypedResults.Created("/meters", new MeterResponse(meter!.SerialNumber, meter.LinkedAt)),
            LinkOutcome.SerialTaken => TypedResults.Conflict(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, ForbidHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct) =>
        CurrentPatient.Id(user) is { } patientId
            ? TypedResults.Ok(await directory.MetersOfAsync(patientId, ct))
            : TypedResults.Forbid();
}
