using System.Globalization;
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
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Test) || request.Test.Length > 40)
        {
            errors["test"] = ["Test is required (max 40 characters)."];
        }

        if (string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Length > 16)
        {
            errors["unit"] = ["Unit is required (max 16 characters)."];
        }

        if (Math.Abs(request.Value) >= 10_000_000m)
        {
            errors["value"] = ["Value is out of range."];
        }

        if (string.IsNullOrWhiteSpace(request.Phone) || request.Phone.Length > 20)
        {
            errors["phone"] = ["Phone is required."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, time.GetUtcNow());
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Result {ResultId} recorded for patient {NationalId}", result.Id, Masking.NationalId(request.NationalId));

        // The result is already saved; a full queue must not fail or stall the request.
        if (!notifications.TryEnqueue(new Notification(request.Phone, $"نتيجة تحليل {result.Test} جاهزة. يمكنك الاطلاع عليها في التطبيق.")))
        {
            logger.LogWarning("SMS queue full; notification for result {ResultId} dropped", result.Id);
        }

        return TypedResults.Created($"/results/{result.Id}", ToResponse(result));
    }

    /// <summary>Newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(LabDbContext db, string? before, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Results.AsNoTracking();
        if (before is not null)
        {
            // The cursor is "<receivedAt>|<id>": the id breaks ties between results with the same timestamp.
            var parts = before.Split('|');
            if (parts.Length != 2
                || !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var cursorId))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["before"] = ["Invalid cursor."] });
            }

            var cursor = parsed.ToUniversalTime();
            query = query.Where(r => r.ReceivedAt < cursor || (r.ReceivedAt == cursor && r.Id < cursorId));
        }

        var page = await query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id).Take(take).ToListAsync(ct);
        var last = page.Count == take ? page[^1] : null;
        var next = last is null
            ? null
            : last.ReceivedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "|" + last.Id.ToString(CultureInfo.InvariantCulture);
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        LookupRequest request, LabClient labs, ILogger<LookupRequest> logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NationalId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["nationalId"] = ["National id is required."] });
        }

        try
        {
            return TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Lab network lookup failed");
            return TypedResults.Problem("The lab network is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
