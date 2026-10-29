using System.Security.Claims;
using FluentValidation;
using LabResults.Application;
using LabResults.Domain;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Api;

public sealed record RecordResultRequest(int PatientId, string NationalId, string Phone, string Test, decimal Value, string Unit, string? Notes);

public sealed record ResultResponse(int Id, int PatientId, string Test, decimal Value, string Unit, string? Notes, string RecordedBy, DateTimeOffset ReceivedAt);

public sealed record ResultPage(IReadOnlyList<ResultResponse> Items, string? Next);

public sealed record LookupRequest(string NationalId);

public static class ResultEndpoints
{
    public const string LookupRateLimit = "lab-order-lookup";

    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder app)
    {
        var clinicians = app.MapGroup("/").RequireAuthorization("clinician");
        clinicians.MapPost("/results", RecordAsync);
        clinicians.MapGet("/results", ListAsync);
        clinicians.MapPost("/lab-orders/lookup", LookupAsync).RequireRateLimiting(LookupRateLimit);
        return app;
    }

    internal static async Task<Results<Created<ResultResponse>, ValidationProblem, UnauthorizedHttpResult>> RecordAsync(
        RecordResultRequest request, ClaimsPrincipal user, IValidator<RecordResultRequest> validator, LabDbContext db,
        TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        // The audit trail is the caller's sub, read here per request; no sub means no result, never "system".
        var sub = user.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(sub) || sub.Length > LabResult.RecordedByMaxLength)
        {
            return TypedResults.Unauthorized();
        }

        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        SaudiMobile.TryNormalize(request.Phone, out var phone);
        var now = time.GetUtcNow();
        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, now, sub);
        db.Results.Add(result);
        db.PendingSms.Add(PendingSms.For(result, phone!, now));
        await db.SaveChangesAsync(ct);

        loggers.CreateLogger(typeof(ResultEndpoints))
            .LogInformation("Result {ResultId} recorded for patient {PatientId}", result.Id, result.PatientId);

        // There is no GET /results/{id}, so no Location header.
        return TypedResults.Created((string?)null, ToResponse(result));
    }

    /// <summary>Newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    internal static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(
        LabDbContext db, string? before, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Results.AsQueryable();
        if (before is not null)
        {
            if (!ResultCursor.TryDecode(before, out var cursor))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["before"] = ["Pass back the next value from the previous page."],
                });
            }

            query = query.Where(r => r.ReceivedAt < cursor.ReceivedAt || (r.ReceivedAt == cursor.ReceivedAt && r.Id < cursor.Id));
        }

        var page = await query
            .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(take)
            .Select(r => new ResultResponse(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt))
            .ToListAsync(ct);
        var next = page.Count == take ? new ResultCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return TypedResults.Ok(new ResultPage(page, next));
    }

    internal static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem>> LookupAsync(
        LookupRequest request, IValidator<LookupRequest> validator, LabClient labs, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid || !NationalId.TryNormalize(request.NationalId, out var nationalId))
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        return TypedResults.Ok(await labs.OrdersAsync(nationalId, ct));
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
