using FluentValidation;
using LabResults.Application;
using LabResults.Domain;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Api;

public static class ResultEndpoints
{
    // Deliberately says nothing about the test: an SMS can be read by anyone holding the phone, and the
    // name of a test (HIV, pregnancy, ...) is health information in itself.
    private const string ResultReadyText = "نتيجة تحليلك جاهزة. يمكنك الاطلاع عليها في التطبيق.";

    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder app)
    {
        var clinicians = app.MapGroup("/").RequireAuthorization("clinician");
        clinicians.MapPost("/results", RecordAsync);
        clinicians.MapGet("/results", ListAsync);
        clinicians.MapPost("/lab-orders/lookup", LookupAsync);
        return app;
    }

    private static async Task<Results<Created<ResultResponse>, ValidationProblem>> RecordAsync(
        RecordResultRequest request, IValidator<RecordResultRequest> validator, LabDbContext db, CurrentUser user,
        NotificationQueue notifications, TimeProvider time, ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, user.Id, time.GetUtcNow());
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);
        loggers.CreateLogger(typeof(ResultEndpoints)).LogInformation("Result {ResultId} recorded for patient {PatientId}", result.Id, result.PatientId);

        await notifications.EnqueueAsync(new Notification(request.Phone, ResultReadyText), ct);
        return TypedResults.Created($"/results/{result.Id}", ToResponse(result));
    }

    /// <summary>
    /// The caller's own results, newest first, a page at a time. Pass the previous page's <c>next</c> as
    /// <c>before</c>.
    /// </summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(
        LabDbContext db, CurrentUser user, string? before, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var recordedBy = user.Id;
        var query = db.Results.AsNoTracking().Where(r => r.RecordedBy == recordedBy);
        if (before is not null)
        {
            if (!ResultCursor.TryParse(before, out var cursor))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(before)] = ["Not a valid page cursor."] });
            }

            query = query.Where(r => r.ReceivedAt < cursor.ReceivedAt || (r.ReceivedAt == cursor.ReceivedAt && r.Id < cursor.Id));
        }

        // One extra row tells us whether another page exists, so the last page doesn't hand out a cursor
        // to an empty one.
        var rows = await query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id).Take(take + 1).ToListAsync(ct);
        var page = rows.Take(take).ToList();
        var next = rows.Count > take ? new ResultCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem>> LookupAsync(
        LookupRequest request, IValidator<LookupRequest> validator, LabClient labs, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        return TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
