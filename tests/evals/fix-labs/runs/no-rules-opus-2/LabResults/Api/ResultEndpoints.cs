using System.Buffers.Text;
using System.Globalization;
using System.Text;
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

/// <summary>Opaque keyset cursor: (receivedAt, id) of the last item on a page, so equal timestamps never skip rows.</summary>
public readonly record struct ResultCursor(DateTimeOffset ReceivedAt, int Id)
{
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{ReceivedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{Id.ToString(CultureInfo.InvariantCulture)}"));

    public static bool TryDecode(string? value, out ResultCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(value) || value.Length > 64)
        {
            return false;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = text.Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return false;
        }

        cursor = new ResultCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        return true;
    }
}

public static partial class ResultValidation
{
    public const int TestMax = 40, UnitMax = 16, NotesMax = 2000;

    // numeric(10,3): at most 7 integer digits and 3 decimals.
    private const decimal ValueLimit = 10_000_000m;

    public static Dictionary<string, string[]> Validate(RecordResultRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (r.PatientId <= 0)
        {
            errors["patientId"] = ["A valid patient id is required."];
        }

        if (!IsNationalId(r.NationalId))
        {
            errors["nationalId"] = ["The national id must be 10 digits."];
        }

        if (r.Phone is null || !Phone().IsMatch(r.Phone))
        {
            errors["phone"] = ["The phone number must be in international format, e.g. +9665XXXXXXXX."];
        }

        if (string.IsNullOrWhiteSpace(r.Test) || r.Test.Length > TestMax)
        {
            errors["test"] = [$"The test is required, at most {TestMax} characters."];
        }

        if (string.IsNullOrWhiteSpace(r.Unit) || r.Unit.Length > UnitMax)
        {
            errors["unit"] = [$"The unit is required, at most {UnitMax} characters."];
        }

        if (Math.Abs(r.Value) >= ValueLimit || r.Value.Scale > 3 && decimal.Round(r.Value, 3) != r.Value)
        {
            errors["value"] = ["The value must have at most 7 integer digits and 3 decimals."];
        }

        if (r.Notes is { Length: > NotesMax })
        {
            errors["notes"] = [$"Notes must be at most {NotesMax} characters."];
        }

        return errors;
    }

    public static bool IsNationalId(string? value) => value is not null && NationalIdPattern().IsMatch(value);

    [GeneratedRegex("^[0-9]{10}$")]
    private static partial Regex NationalIdPattern();

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$")]
    private static partial Regex Phone();
}

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
        var errors = ResultValidation.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = LabResult.Record(request.PatientId, request.Test.Trim(), request.Value, request.Unit.Trim(), request.Notes, time.GetUtcNow());
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Result {ResultId} recorded for patient {NationalId}", result.Id, Masking.NationalId(request.NationalId));

        // The SMS goes over an untrusted channel to a phone others may see: it must not reveal which test was done.
        // The result is already saved, so queueing must neither block nor fail the request.
        if (!notifications.TryEnqueue(new Notification(request.Phone, "نتيجة تحليلك جاهزة. يمكنك الاطلاع عليها في التطبيق.")))
        {
            logger.LogWarning("Notification queue full; SMS for result {ResultId} was not sent", result.Id);
        }

        return TypedResults.Created($"/results/{result.Id}", ToResponse(result));
    }

    /// <summary>Newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(
        LabDbContext db, string? before, int? limit, int? patientId, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Results.AsNoTracking();
        if (patientId is not null)
        {
            query = query.Where(r => r.PatientId == patientId);
        }

        if (before is not null)
        {
            if (!ResultCursor.TryDecode(before, out var cursor))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["before"] = ["Invalid cursor."] });
            }

            var at = cursor.ReceivedAt;
            var id = cursor.Id;
            query = query.Where(r => r.ReceivedAt < at || (r.ReceivedAt == at && r.Id < id));
        }

        // Fetch one extra row to know whether there is a next page without an empty trailing page.
        var rows = await query
            .OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id)
            .Take(take + 1)
            .ToListAsync(ct);
        var page = rows.Count > take ? rows[..take] : rows;
        var next = rows.Count > take ? new ResultCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        LookupRequest request, LabClient labs, ILogger<LookupRequest> logger, CancellationToken ct)
    {
        // Validating the format also keeps values like ".." from rewriting the path sent to the lab network.
        if (!ResultValidation.IsNationalId(request.NationalId))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["nationalId"] = ["The national id must be 10 digits."] });
        }

        try
        {
            return TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || ex is TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Lab network lookup failed");
            return TypedResults.Problem("The lab network is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
