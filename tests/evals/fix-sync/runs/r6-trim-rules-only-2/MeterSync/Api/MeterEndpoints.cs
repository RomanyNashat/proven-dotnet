using System.Security.Claims;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MeterSync.Api;

public sealed record LinkMeterRequest(string? NationalId, string? SerialNumber);

public sealed record MeterResponse(string SerialNumber, DateTimeOffset LinkedAt);

public static class MeterEndpoints
{
    private const int MaxNationalIdLength = 10;
    private const int MaxSerialNumberLength = 40;

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

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.SerialNumber) || request.SerialNumber.Length > MaxSerialNumberLength)
        {
            errors[nameof(request.SerialNumber)] = [$"A serial number of up to {MaxSerialNumberLength} characters is required."];
        }

        if (string.IsNullOrWhiteSpace(request.NationalId) || request.NationalId.Length > MaxNationalIdLength)
        {
            errors[nameof(request.NationalId)] = [$"A national id of up to {MaxNationalIdLength} characters is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (outcome, meter) = await linker.LinkAsync(callerId, request.NationalId!, request.SerialNumber!, ct);
        return outcome switch
        {
            LinkOutcome.Linked => TypedResults.Created("/meters", new MeterResponse(meter!.SerialNumber, meter.LinkedAt)),
            LinkOutcome.AlreadyLinked => TypedResults.Conflict(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, ForbidHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct)
    {
        if (CurrentPatient.Id(user) is not { } callerId)
        {
            return TypedResults.Forbid();
        }

        var meters = await directory.MetersOfAsync(callerId, ct);
        return TypedResults.Ok<IReadOnlyList<MeterResponse>>(meters.Select(m => new MeterResponse(m.SerialNumber, m.LinkedAt)).ToList());
    }
}
