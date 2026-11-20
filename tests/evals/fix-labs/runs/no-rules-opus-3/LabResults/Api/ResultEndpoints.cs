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
    private const string LogCategory = "LabResults.Api.Results";

    /// <summary>
    /// Deliberately generic: an SMS can be read on a lock screen or by whoever holds the phone, so it must not say
    /// which test was done. ("Your lab result is ready. You can view it in the app.")
    /// </summary>
    internal const string ResultReadySms = "نتيجة التحليل جاهزة. يمكنك الاطلاع عليها في التطبيق.";

    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder app)
    {
        var clinicians = app.MapGroup("/").RequireAuthorization("clinician");
        clinicians.MapPost("/results", RecordAsync);
        clinicians.MapGet("/results", ListAsync);
        clinicians.MapGet("/results/{id:int}", GetAsync);
        clinicians.MapPost("/lab-orders/lookup", LookupAsync);
        return app;
    }

    private static async Task<Results<Created<ResultResponse>, ValidationProblem>> RecordAsync(
        RecordResultRequest request, LabDbContext db, NotificationQueue notifications, TimeProvider time,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // PostgreSQL keeps microseconds; truncate now so the response matches what later reads return.
        var now = time.GetUtcNow();
        var receivedAt = new DateTimeOffset(now.UtcTicks - (now.UtcTicks % 10), TimeSpan.Zero);
        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, receivedAt);
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);

        // The national ID and phone number stay out of the logs; the internal patient ID is enough to trace.
        var logger = loggers.CreateLogger(LogCategory);
        logger.LogInformation("Result {ResultId} recorded for patient {PatientId}", result.Id, result.PatientId);

        // The result is already saved: from here on nothing may fail the request (or a retry would record it twice).
        if (!notifications.TryEnqueue(new Notification(request.Phone, ResultReadySms)))
        {
            logger.LogWarning("Notification queue full; no SMS for result {ResultId}", result.Id);
        }

        return TypedResults.Created($"/results/{result.Id}", ToResponse(result));
    }

    private static async Task<Results<Ok<ResultResponse>, NotFound>> GetAsync(int id, LabDbContext db, CancellationToken ct)
    {
        var result = await db.Results.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(result));
    }

    /// <summary>Newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(
        LabDbContext db, string? before, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Results.AsNoTracking();
        if (!string.IsNullOrEmpty(before))
        {
            if (!PageCursor.TryParse(before, out var cursor))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["before"] = ["Not a valid page cursor."] });
            }

            var (at, id) = cursor;
            query = query.Where(r => r.ReceivedAt < at || (r.ReceivedAt == at && r.Id < id));
        }

        // One extra row tells us whether there is a next page, so the last page never hands out a dangling cursor.
        var rows = await query
            .OrderByDescending(r => r.ReceivedAt)
            .ThenByDescending(r => r.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var page = rows.Count > take ? rows[..take] : rows;
        var next = rows.Count > take ? PageCursor.Format(page[^1].ReceivedAt, page[^1].Id) : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        LookupRequest request, LabClient labs, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!IsNationalId(request.NationalId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["nationalId"] = ["Must be a 10-digit national ID."] });
        }

        try
        {
            return TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));
        }
        catch (LabUnavailableException ex)
        {
            loggers.CreateLogger(LogCategory).LogWarning(ex, "Lab order lookup failed for {NationalId}", Masking.NationalId(request.NationalId));
            return TypedResults.Problem("The lab network is unavailable. Try again later.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    internal static Dictionary<string, string[]> Validate(RecordResultRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.PatientId <= 0)
        {
            errors["patientId"] = ["Must be a positive patient ID."];
        }

        if (!IsNationalId(request.NationalId))
        {
            errors["nationalId"] = ["Must be a 10-digit national ID."];
        }

        if (request.Phone is null || !PhoneNumber().IsMatch(request.Phone))
        {
            errors["phone"] = ["Must be an international phone number, e.g. +9665XXXXXXXX."];
        }

        if (string.IsNullOrWhiteSpace(request.Test) || request.Test.Length > LabResult.MaxTestLength)
        {
            errors["test"] = [$"Required, at most {LabResult.MaxTestLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Length > LabResult.MaxUnitLength)
        {
            errors["unit"] = [$"Required, at most {LabResult.MaxUnitLength} characters."];
        }

        // Out-of-range values would fail in the database; extra decimals would be silently rounded off a clinical value.
        if (Math.Abs(request.Value) > LabResult.MaxAbsValue || request.Value != Math.Round(request.Value, LabResult.MaxValueScale))
        {
            errors["value"] = [$"At most 7 digits before and {LabResult.MaxValueScale} after the decimal point."];
        }

        if (request.Notes is { Length: > LabResult.MaxNotesLength })
        {
            errors["notes"] = [$"At most {LabResult.MaxNotesLength} characters."];
        }

        return errors;
    }

    internal static bool IsNationalId(string? value) => value is not null && NationalIdPattern().IsMatch(value);

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);

    [GeneratedRegex(@"^[0-9]{10}\z")]
    private static partial Regex NationalIdPattern();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}\z")]
    private static partial Regex PhoneNumber();
}

/// <summary>
/// Position in the newest-first listing: the (ReceivedAt, Id) of the last item returned.
/// Formatted as <c>2026-11-20T10:15:30.1234567Z_42</c> (no characters that need URL-escaping).
/// </summary>
internal static class PageCursor
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static string Format(DateTimeOffset receivedAt, int id) =>
        receivedAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture) + "_" + id.ToString(CultureInfo.InvariantCulture);

    public static bool TryParse(string value, out (DateTimeOffset At, int Id) cursor)
    {
        cursor = default;
        if (value.Length > 64)
        {
            return false;
        }

        var separator = value.LastIndexOf('_');
        if (separator > 0
            && DateTime.TryParseExact(value[..separator], TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            && int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            cursor = (new DateTimeOffset(at, TimeSpan.Zero), id);
            return true;
        }

        // Also accept a bare timestamp (the older cursor format); a '+' in an unescaped query string arrives as a space.
        if (DateTimeOffset.TryParse(value.Replace(' ', '+'), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            cursor = (timestamp.ToUniversalTime(), int.MinValue);
            return true;
        }

        return false;
    }
}
