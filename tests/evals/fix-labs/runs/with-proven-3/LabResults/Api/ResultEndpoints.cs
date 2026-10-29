using LabResults.Application;
using LabResults.Domain;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Api;

public static class ResultEndpoints
{
    public const string ClinicianPolicy = "clinician";

    private const string LogCategory = "LabResults.Api.Results";

    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder app)
    {
        var clinicians = app.MapGroup("/").RequireAuthorization(ClinicianPolicy);
        clinicians.MapPost("/results", RecordAsync)
            .AddEndpointFilter<ValidationFilter<RecordResultRequest>>()
            .RequireRateLimiting(PerCallerRateLimits.Record);
        clinicians.MapGet("/results", ListAsync);
        clinicians.MapPost("/lab-orders/lookup", LookupAsync)
            .AddEndpointFilter<ValidationFilter<LookupRequest>>()
            .RequireRateLimiting(PerCallerRateLimits.Lookup);
        return app;
    }

    private static async Task<Created<ResultResponse>> RecordAsync(
        RecordResultRequest request, LabDbContext db, NotificationQueue notifications, TimeProvider time,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, time.GetUtcNow());
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);

        // PatientId only: the national ID is not stored and must not reach the logs.
        var logger = loggers.CreateLogger(LogCategory);
        logger.LogInformation("Result {ResultId} recorded for patient {PatientId}", result.Id, result.PatientId);
        if (!notifications.TryEnqueue(Notification.ResultReady(SaudiMobile.Normalize(request.Phone))))
        {
            logger.LogWarning("SMS for result {ResultId} not queued: the queue is full or shutting down", result.Id);
        }

        // No Location: there is no GET /results/{id} for it to point at.
        return TypedResults.Created((string?)null, ToResponse(result));
    }

    /// <summary>Newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(
        LabDbContext db, CurrentUser user, ILoggerFactory loggers, string? before, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Results.AsQueryable();
        if (before is not null)
        {
            if (!ResultCursor.TryDecode(before, out var cursor))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["before"] = ["Not a cursor from this API."] });
            }

            query = query.Where(r => r.ReceivedAt < cursor.At || (r.ReceivedAt == cursor.At && r.Id < cursor.Id));
        }

        var page = await query
            .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(take)
            .Select(r => new ResultResponse(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt))
            .ToListAsync(ct);
        var next = page.Count == take ? new ResultCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;

        loggers.CreateLogger(LogCategory).LogInformation("Results read by {CallerId}: {Count} rows", user.Id, page.Count);
        return TypedResults.Ok(new ResultPage(page, next));
    }

    private static async Task<Ok<IReadOnlyList<LabOrder>>> LookupAsync(
        LookupRequest request, LabClient labs, CurrentUser user, ILoggerFactory loggers, CancellationToken ct)
    {
        var orders = await labs.OrdersAsync(NationalId.Normalize(request.NationalId), ct);
        loggers.CreateLogger(LogCategory).LogInformation("Lab orders looked up by {CallerId}: {Count} orders", user.Id, orders.Count);
        return TypedResults.Ok(orders);
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
