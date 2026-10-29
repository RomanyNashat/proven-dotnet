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
    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder app)
    {
        var clinicians = app.MapGroup("/").RequireAuthorization("clinician");
        clinicians.MapPost("/results", RecordAsync);
        clinicians.MapGet("/results", ListAsync);
        clinicians.MapPost("/lab-orders/lookup", LookupAsync);
        return app;
    }

    private static async Task<Results<Created<ResultResponse>, ValidationProblem>> RecordAsync(
        RecordResultRequest request, LabDbContext db, NotificationQueue notifications, TimeProvider time,
        ILogger<RecordResultRequest> logger, CancellationToken ct)
    {
        var errors = RecordResultValidator.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, time.GetUtcNow());
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Result {ResultId} recorded for patient {PatientId}", result.Id, result.PatientId);

        if (!notifications.TryEnqueue(Notification.ResultReady(request.Phone)))
        {
            logger.LogWarning("SMS queue full; no notification for result {ResultId}", result.Id);
        }

        return TypedResults.Created($"/results/{result.Id}", ToResponse(result));
    }

    /// <summary>
    /// The caller's own results, newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.
    /// </summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(
        LabDbContext db, CurrentUser user, string? before, int? limit, CancellationToken ct)
    {
        ResultCursor? cursor = null;
        if (before is not null && !ResultCursor.TryParse(before, out cursor))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(before)] = ["Not a cursor from a previous page."] });
        }

        var take = Math.Clamp(limit ?? 50, 1, 200);
        var recordedBy = user.Id;
        var query = db.Results.AsNoTracking().Where(r => r.RecordedBy == recordedBy);
        if (cursor is not null)
        {
            query = query.Where(r => r.ReceivedAt < cursor.ReceivedAt || (r.ReceivedAt == cursor.ReceivedAt && r.Id < cursor.Id));
        }

        // One extra row tells whether another page exists, so the last page has no `next`.
        var rows = await query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id).Take(take + 1).ToListAsync(ct);
        var page = rows.Count > take ? rows[..take] : rows;
        var next = rows.Count > take ? new ResultCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem>> LookupAsync(
        LookupRequest request, LabClient labs, CancellationToken ct)
    {
        var errors = LookupRequestValidator.Validate(request);
        return errors.Count > 0
            ? TypedResults.ValidationProblem(errors)
            : TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
