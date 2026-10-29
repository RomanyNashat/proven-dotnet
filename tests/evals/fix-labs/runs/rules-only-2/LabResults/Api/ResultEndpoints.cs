using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LabResults.Application;
using LabResults.Domain;
using LabResults.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LabResults.Api;

public sealed record RecordResultRequest(
    [property: Range(1, int.MaxValue)] int PatientId,
    [property: Required, RegularExpression(Patterns.NationalId)] string NationalId,
    [property: Required, RegularExpression(Patterns.Phone, ErrorMessage = "Phone must be in international format, e.g. +9665XXXXXXXX.")] string Phone,
    [property: Required, StringLength(LabResultLimits.Test)] string Test,
    // numeric(10,3): anything larger overflows the column.
    [property: Range(typeof(decimal), "-9999999.999", "9999999.999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal Value,
    [property: Required, StringLength(LabResultLimits.Unit)] string Unit,
    [property: StringLength(LabResultLimits.Notes)] string? Notes);

public sealed record ResultResponse(int Id, int PatientId, string Test, decimal Value, string Unit, string? Notes, string RecordedBy, DateTimeOffset ReceivedAt);

public sealed record ResultPage(IReadOnlyList<ResultResponse> Items, string? Next);

public sealed record LookupRequest([property: Required, RegularExpression(Patterns.NationalId)] string NationalId);

internal static class Patterns
{
    public const string NationalId = "^[0-9]{10}$";
    public const string Phone = @"^\+[1-9][0-9]{7,14}$";
}

public static class ResultEndpoints
{
    public static IEndpointRouteBuilder MapResultEndpoints(this IEndpointRouteBuilder app)
    {
        var clinicians = app.MapGroup("/").RequireAuthorization(AuthPolicies.Clinician);
        clinicians.MapPost("/results", RecordAsync);
        clinicians.MapGet("/results", ListAsync);
        clinicians.MapPost("/lab-orders/lookup", LookupAsync);
        return app;
    }

    private static async Task<Results<Created<ResultResponse>, ValidationProblem>> RecordAsync(
        RecordResultRequest request, ClaimsPrincipal user, LabDbContext db, NotificationQueue notifications, TimeProvider time,
        ILoggerFactory loggers, CancellationToken ct)
    {
        if (!RequestValidation.TryValidate(request, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = LabResult.Record(
            request.PatientId, request.Test, request.Value, request.Unit, request.Notes, ClinicianId(user), time.GetUtcNow());
        db.Results.Add(result);
        await db.SaveChangesAsync(ct);

        var logger = loggers.CreateLogger(typeof(ResultEndpoints));
        logger.LogInformation("Result {ResultId} recorded for patient {NationalId}", result.Id, Masking.NationalId(request.NationalId));

        // The SMS goes to a phone number, not to an authenticated patient: it says a result is ready, never which test.
        if (!notifications.TryEnqueue(new Notification(request.Phone, "نتيجة تحليلك جاهزة. يمكنك الاطلاع عليها في التطبيق.")))
        {
            logger.LogWarning("SMS queue full; no notification for result {ResultId}", result.Id);
        }

        return TypedResults.Created($"/results/{result.Id}", ToResponse(result));
    }

    /// <summary>The caller's own results, newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    private static async Task<Results<Ok<ResultPage>, ValidationProblem>> ListAsync(
        ClaimsPrincipal user, LabDbContext db, string? before, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var clinician = ClinicianId(user);
        var query = db.Results.AsNoTracking().Where(r => r.RecordedBy == clinician);
        if (before is not null)
        {
            if (!ResultCursor.TryParse(before, out var cursor))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(before)] = ["Pass back the previous page's next."] });
            }

            query = query.Where(r => r.ReceivedAt < cursor.ReceivedAt || (r.ReceivedAt == cursor.ReceivedAt && r.Id < cursor.Id));
        }

        // One extra row tells whether there is a next page, so the last page doesn't hand out a cursor to an empty one.
        var rows = await query.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Id).Take(take + 1).ToListAsync(ct);
        var page = rows.Take(take).ToList();
        var next = rows.Count > take ? new ResultCursor(page[^1].ReceivedAt, page[^1].Id).Encode() : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Results<Ok<IReadOnlyList<LabOrder>>, ValidationProblem, ProblemHttpResult>> LookupAsync(
        LookupRequest request, LabClient labs, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!RequestValidation.TryValidate(request, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        try
        {
            return TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            loggers.CreateLogger(typeof(ResultEndpoints)).LogError(ex, "Lab network lookup failed");
            return TypedResults.Problem(title: "The lab network is unavailable.", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    // The clinician policy requires "sub", so it is always present here.
    private static string ClinicianId(ClaimsPrincipal user) => user.FindFirstValue(AuthPolicies.SubjectClaim)!;

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
