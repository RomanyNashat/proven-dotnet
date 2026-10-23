using System.Security.Claims;
using MeterSync.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MeterSync.Api;

public sealed record LinkMeterRequest(string NationalId, string SerialNumber);

public sealed record MeterResponse(string SerialNumber, DateTimeOffset LinkedAt);

public static class MeterEndpoints
{
    public const string LinkMeterRateLimit = "link-meter";

    public static IEndpointRouteBuilder MapMeterEndpoints(this IEndpointRouteBuilder app)
    {
        var meters = app.MapGroup("/meters");
        meters.MapPost("/", LinkAsync).RequireRateLimiting(LinkMeterRateLimit);
        meters.MapGet("/", ListAsync);
        app.MapGet("/health/live", () => TypedResults.Ok()).AllowAnonymous();
        return app;
    }

    private static async Task<Results<Created<MeterResponse>, NotFound, Conflict, ValidationProblem, UnauthorizedHttpResult>> LinkAsync(
        LinkMeterRequest request, ClaimsPrincipal user, MeterLinking linking, CancellationToken ct)
    {
        if (LinkMeterValidation.Validate(request) is { } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!CurrentPatient.TryGetId(user, out var callerId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await linking.LinkAsync(callerId, request.NationalId, request.SerialNumber, ct);
        return result.Outcome switch
        {
            LinkOutcome.Linked => TypedResults.Created("/meters", new MeterResponse(result.Meter!.SerialNumber, result.Meter.LinkedAt)),
            LinkOutcome.AlreadyLinked => TypedResults.Conflict(),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<Ok<IReadOnlyList<MeterResponse>>, UnauthorizedHttpResult>> ListAsync(
        ClaimsPrincipal user, IPatientMeters meters, CancellationToken ct)
    {
        if (!CurrentPatient.TryGetId(user, out var patientId))
        {
            return TypedResults.Unauthorized();
        }

        var linked = await meters.MetersOfAsync(patientId, ct);
        return TypedResults.Ok<IReadOnlyList<MeterResponse>>(linked.Select(m => new MeterResponse(m.SerialNumber, m.LinkedAt)).ToList());
    }
}
