using System.Security.Claims;
using MeterSync.Application;
using MeterSync.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

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

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, ForbidHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, MeterLinker linker, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var patientId))
        {
            return TypedResults.Forbid();
        }

        var errors = LinkMeterRequestValidator.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (outcome, meter) = await linker.LinkAsync(patientId, request.NationalId, request.SerialNumber, ct);
        return outcome switch
        {
            LinkOutcome.Linked => TypedResults.Created("/meters", new MeterResponse(meter!.SerialNumber, meter.LinkedAt)),
            LinkOutcome.AlreadyLinked => TypedResults.Conflict(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, ForbidHttpResult>> ListAsync(
        ClaimsPrincipal user, PatientDirectory directory, CancellationToken ct) =>
        CurrentPatient.TryGetId(user, out var patientId)
            ? TypedResults.Ok(await directory.MetersOfAsync(patientId, ct))
            : TypedResults.Forbid();
}
