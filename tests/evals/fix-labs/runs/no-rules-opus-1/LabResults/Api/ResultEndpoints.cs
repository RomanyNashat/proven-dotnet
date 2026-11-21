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
    /// <summary>
    /// Deliberately generic: an SMS can be read on a lock screen or by whoever holds the phone,
    /// so it must not say which test was done ("Your lab result is ready. You can view it in the app.").
    /// </summary>
    public const string ResultReadySms = "نتيجة تحليلك جاهزة. يمكنك الاطلاع عليها في التطبيق.";

    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder app)
    {
        var clinicians = app.MapGroup("/").RequireAuthorization("clinician");
        clinicians.MapPost("/results", RecordAsync);
        clinicians.MapGet("/results", ListAsync);
        clinicians.MapPost("/lab-orders/lookup", LookupAsync);
        return app;
    }

    private static async Task<Results<Created<ResultResponse>, ValidationProblem>> RecordAsync(
        RecordResultRequest request, LabDbContext db, NotificationSignal signal, TimeProvider time,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var errors = Validation.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var now = time.GetUtcNow();
        var result = LabResult.Record(request.PatientId, request.Test.Trim(), request.Value, request.Unit.Trim(), request.Notes, now);
        db.Results.Add(result);

        // Outbox: the SMS is committed together with the result, so it survives restarts and is never sent
        // for a result that failed to save.
        db.SmsOutbox.Add(SmsMessage.Create(request.Phone, ResultReadySms, now));
        await db.SaveChangesAsync(ct);
        signal.Notify();

        // No national ID or phone in the logs: the internal patient id is enough to trace it.
        loggers.CreateLogger("LabResults.Results").LogInformation("Result {ResultId} recorded for patient {PatientId}", result.Id, result.PatientId);
        return TypedResults.Created($"/results/{result.Id}", ToResponse(result));
    }

    /// <summary>Newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(
        LabDbContext db, string? before, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Results.AsNoTracking();
        if (before is not null)
        {
            if (!PageCursor.TryDecode(before, out var cursor))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["before"] = ["Not a valid page cursor."] });
            }

            query = query.Where(r => r.ReceivedAt < cursor.ReceivedAt || (r.ReceivedAt == cursor.ReceivedAt && r.Id < cursor.Id));
        }

        // Fetch one extra row to know whether there is a next page, instead of guessing from a full page.
        var rows = await query
            .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(take + 1)
            .ToListAsync(ct);
        var page = rows.Count > take ? rows[..take] : rows;
        var next = rows.Count > take ? new PageCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        LookupRequest request, LabClient labs, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!Validation.IsNationalId(request?.NationalId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["nationalId"] = ["Not a valid national ID."] });
        }

        try
        {
            return TypedResults.Ok(await labs.OrdersAsync(request!.NationalId, ct));
        }
        catch (LabUnavailableException ex)
        {
            loggers.CreateLogger("LabResults.Labs").LogWarning(ex, "Lab order lookup failed");
            return TypedResults.Problem("The lab network is unavailable. Try again shortly.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
