using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const decimal MaxValue = 9_999_999.999m;

    private static readonly Regex PhonePattern = new(@"^\+?[0-9]{7,15}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NationalIdPattern = new(@"^[0-9]{10}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
            errors["Test"] = ["Test is required (max 40 characters)."];
        }

        if (string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Length > 16)
        {
            errors["Unit"] = ["Unit is required (max 16 characters)."];
        }

        if (request.PatientId <= 0)
        {
            errors["PatientId"] = ["PatientId is required."];
        }

        if (request.NationalId is null || !NationalIdPattern.IsMatch(request.NationalId))
        {
            errors["NationalId"] = ["NationalId must be 10 digits."];
        }

        if (request.Phone is null || !PhonePattern.IsMatch(request.Phone))
        {
            errors["Phone"] = ["Phone must be a phone number in international format."];
        }

        if (Math.Abs(request.Value) > MaxValue)
        {
            errors["Value"] = ["Value is out of range."];
        }

        if (request.Notes is { Length: > 2000 })
        {
            errors["Notes"] = ["Notes are limited to 2000 characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, time.GetUtcNow());
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Result {ResultId} recorded for patient {NationalId}", result.Id, Masking.NationalId(request.NationalId!));

        // The result is already saved: a full queue must not turn that into a failed request.
        if (!notifications.TryEnqueue(new Notification(request.Phone!,$"نتيجة تحليل {result.Test} جاهزة. يمكنك الاطلاع عليها في التطبيق.")))
        {
            logger.LogWarning("Notification queue full; SMS for result {ResultId} dropped", result.Id);
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
            // The cursor is "<receivedAt>|<id>": timestamps alone can tie, which would skip rows between pages.
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

        var rows = await query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id).Take(take + 1).ToListAsync(ct);
        var page = rows.Take(take).ToList();
        var next = rows.Count > take
            ? page[^1].ReceivedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "|" + page[^1].Id.ToString(CultureInfo.InvariantCulture)
            : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        LookupRequest request, LabClient labs, ILogger<LookupRequest> logger, CancellationToken ct)
    {
        if (request.NationalId is null || !NationalIdPattern.IsMatch(request.NationalId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["NationalId"] = ["NationalId must be 10 digits."] });
        }

        try
        {
            return TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException) && !ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Lab network lookup failed");
            return TypedResults.Problem("The lab network is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
