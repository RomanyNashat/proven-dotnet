using System.Globalization;
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

public static partial class ResultEndpoints
{
    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex NationalIdPattern();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex PhonePattern();

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
        if (request.PatientId <= 0)
        {
            errors["patientId"] = ["A patient is required."];
        }

        if (string.IsNullOrWhiteSpace(request.NationalId) || !NationalIdPattern().IsMatch(request.NationalId))
        {
            errors["nationalId"] = ["National id must be 10 digits."];
        }

        if (string.IsNullOrWhiteSpace(request.Phone) || !PhonePattern().IsMatch(request.Phone))
        {
            errors["phone"] = ["Phone must be in international format, e.g. +9665XXXXXXXX."];
        }

        if (string.IsNullOrWhiteSpace(request.Test) || request.Test.Length > 40)
        {
            errors["test"] = ["Test is required (max 40 characters)."];
        }

        if (string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Length > 16)
        {
            errors["unit"] = ["Unit is required (max 16 characters)."];
        }

        if (Math.Abs(request.Value) >= 10_000_000m || decimal.Round(request.Value, 3) != request.Value)
        {
            errors["value"] = ["Value must have at most 7 integer and 3 decimal digits."];
        }

        if (request.Notes is { Length: > 2000 })
        {
            errors["notes"] = ["Notes are limited to 2000 characters."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, time.GetUtcNow());
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Result {ResultId} recorded for patient {NationalId}", result.Id, Masking.NationalId(request.NationalId));

        // The result is already saved; a full queue must not fail the request.
        if (!notifications.TryEnqueue(new Notification(request.Phone, $"نتيجة تحليل {result.Test} جاهزة. يمكنك الاطلاع عليها في التطبيق.")))
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
            if (!TryParseCursor(before, out var at, out var id))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["before"] = ["Invalid cursor."] });
            }

            query = query.Where(r => r.ReceivedAt < at || (r.ReceivedAt == at && r.Id < id));
        }

        var page = await query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id).Take(take).ToListAsync(ct);
        var next = page.Count == take ? FormatCursor(page[^1]) : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        LookupRequest request, LabClient labs, ILogger<LookupRequest> logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NationalId) || !NationalIdPattern().IsMatch(request.NationalId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["nationalId"] = ["National id must be 10 digits."] });
        }

        try
        {
            return TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogError("Lab network lookup failed: {Error}", ex.GetType().Name);
            return TypedResults.Problem("The lab network is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // Cursor is "<receivedAt ticks>.<id>": the id breaks ties between results received at the same instant.
    private static string FormatCursor(LabResult r) => $"{r.ReceivedAt.UtcTicks}.{r.Id}";

    private static bool TryParseCursor(string value, out DateTimeOffset at, out int id)
    {
        at = default;
        id = 0;
        var parts = value.Split('.');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out id))
        {
            return false;
        }

        at = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
