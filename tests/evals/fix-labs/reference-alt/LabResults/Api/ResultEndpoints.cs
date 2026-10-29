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
        RecordResultRequest request, LabDbContext db, TimeProvider time,
        ILogger<RecordResultRequest> logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Test) || request.Test.Length > 40 || string.IsNullOrWhiteSpace(request.Unit) || request.Unit.Length > 16)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Test"] = ["Test and unit are required."] });
        }

        var result = LabResult.Record(request.PatientId, request.Test, request.Value, request.Unit, request.Notes, time.GetUtcNow());
        db.Results.Add(result);
        db.Sms.Add(new PendingSms(request.Phone, $"نتيجة تحليل {result.Test} جاهزة. يمكنك الاطلاع عليها في التطبيق."));
        await db.SaveChangesAsync(ct);   // the result and its SMS together
        logger.LogInformation("Result {ResultId} recorded for patient {NationalId}", result.Id, Masking.NationalId(request.NationalId));

        return TypedResults.Created($"/results/{result.Id}", ToResponse(result));
    }

    /// <summary>Newest first, a page at a time. Pass the previous page's <c>next</c> as <c>before</c>.</summary>
    private static async Task<Ok<ResultPage>> ListAsync(LabDbContext db, string? before, int? limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Results.AsNoTracking();
        // Ids follow the order results were saved in and are unique, so they page without gaps or repeats.
        if (before is not null && int.TryParse(before, NumberStyles.None, CultureInfo.InvariantCulture, out var lastId))
        {
            query = query.Where(r => r.Id < lastId);
        }

        var page = await query.OrderByDescending(r => r.Id).Take(take).ToListAsync(ct);
        var next = page.Count == take ? page[^1].Id.ToString(CultureInfo.InvariantCulture) : null;
        return TypedResults.Ok(new ResultPage([.. page.Select(ToResponse)], next));
    }

    private static async Task<Ok<IReadOnlyList<LabOrder>>> LookupAsync(LookupRequest request, LabClient labs, CancellationToken ct) =>
        TypedResults.Ok(await labs.OrdersAsync(request.NationalId, ct));

    private static ResultResponse ToResponse(LabResult r) =>
        new(r.Id, r.PatientId, r.Test, r.Value, r.Unit, r.Notes, r.RecordedBy, r.ReceivedAt);
}
