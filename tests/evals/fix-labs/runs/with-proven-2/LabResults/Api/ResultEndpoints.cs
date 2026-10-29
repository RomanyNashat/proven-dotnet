using FluentValidation;
using LabResults.Application;
using LabResults.Domain;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Api;

public static class ResultEndpoints
{
    public const string ClinicianPolicy = "clinician";
    public const string LookupRateLimit = "lab-lookup";

    private const string LogCategory = "LabResults.Api.ResultEndpoints";

    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder app)
    {
        var clinicians = app.MapGroup("/").RequireAuthorization(ClinicianPolicy);
        clinicians.MapPost("/results", RecordAsync);
        clinicians.MapGet("/results", ListAsync);
        clinicians.MapPost("/lab-orders/lookup", LookupAsync).RequireRateLimiting(LookupRateLimit);
        return app;
    }

    /// <summary>Records a result and, in the same save, the SMS that tells the patient it's ready.</summary>
    private static async Task<Results<Created<ResultResponse>, ValidationProblem>> RecordAsync(
        RecordResultRequest request, IValidator<RecordResultRequest> validator, LabDbContext db, TimeProvider time,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        var now = time.GetUtcNow();
        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, now);
        db.Results.Add(result);
        db.SmsOutbox.Add(SmsOutboxMessage.ResultReady(request.Phone, now));
        await db.SaveChangesAsync(ct);

        // The patient id, never the national ID: the log is read by more people than the record.
        loggers.CreateLogger(LogCategory).LogInformation("Result {ResultId} recorded for patient {PatientId}", result.Id, result.PatientId);

        // 201 without a Location: there is no GET /results/{id} to point at.
        return TypedResults.Created((string?)null, ToResponse(result));
    }

    /// <summary>The caller's results, newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem, UnauthorizedHttpResult>> ListAsync(
        string? before, int? limit, LabDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (db.CallerId is not { } caller)
        {
            return TypedResults.Unauthorized();
        }

        ResultCursor? cursor = null;
        if (before is not null && !ResultCursor.TryParse(before, out cursor))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(before)] = ["Not a cursor from a previous page."] });
        }

        var take = Math.Clamp(limit ?? 50, 1, 200);

        // Only the results this clinician recorded: recordedBy is the only ownership data the service has.
        var query = db.Results.AsNoTracking().Where(r => r.RecordedBy == caller);
        if (cursor is not null)
        {
            query = query.Where(r => r.ReceivedAt < cursor.ReceivedAt || (r.ReceivedAt == cursor.ReceivedAt && r.Id < cursor.Id));
        }

        var page = await query
            .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(take)
            .Select(r => new ResultResponse(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt))
            .ToListAsync(ct);

        db.AccessAudit.Add(AccessAuditEntry.Record(caller, AccessAuditEntry.ListResults, null, page.Count, time.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        var next = page.Count == take ? new ResultCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return TypedResults.Ok(new ResultPage(page, next));
    }

    /// <summary>The lab's open orders for a patient. Each lookup is audited and rate-limited per clinician.</summary>
    private static async Task<Results<Ok<IReadOnlyList<LabOrderResponse>>, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> LookupAsync(
        LookupRequest request, IValidator<LookupRequest> validator, LabDbContext db, LabClient labs, TimeProvider time, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return TypedResults.ValidationProblem(validation.ToDictionary());
        }

        if (db.CallerId is not { } caller)
        {
            return TypedResults.Unauthorized();
        }

        // Audited before the call, so a lookup counts even when the lab is down.
        db.AccessAudit.Add(AccessAuditEntry.Record(caller, AccessAuditEntry.LookupOrders, Masking.NationalId(request.NationalId), 0, time.GetUtcNow()));
        await db.SaveChangesAsync(ct);

        var lookup = await labs.OrdersAsync(request.NationalId, ct);
        if (!lookup.Available)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The lab network is unavailable. Try again shortly.");
        }

        IReadOnlyList<LabOrderResponse> orders = [.. lookup.Orders.Select(o => new LabOrderResponse(o.OrderId, o.Test, o.OrderedAt))];
        return TypedResults.Ok(orders);
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
