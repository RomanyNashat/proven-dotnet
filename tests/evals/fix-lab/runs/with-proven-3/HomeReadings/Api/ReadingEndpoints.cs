using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HomeReadings.Api;

public static class ReadingEndpoints
{
    // The photo plus the multipart framing.
    private const long PhotoRequestLimit = PhotoStore.MaxBytes + 64 * 1024;

    public static void MapReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var readings = app.MapGroup("/readings").RequireAuthorization(PatientClaims.Policy);

        readings.MapPost("/", Add);
        readings.MapGet("/{id:int}", async Task<Results<Ok<ReadingView>, NotFound>> (int id, ReadingQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());
        readings.MapGet("/today", async (ReadingQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.TodayAsync(ct)));
        readings.MapGet("/latest", async Task<Results<Ok<ReadingView>, NotFound>> (ReadingQueries queries, CancellationToken ct) =>
            await queries.LatestAsync(ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        // Bearer tokens only, no cookies, so there is no cross-site request to forge.
        readings.MapPost("/{id:int}/photo", AttachPhoto)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PhotoRequestLimit))
            .WithFormOptions(multipartBodyLengthLimit: PhotoRequestLimit);
    }

    private static async Task<Results<Created<SavedReading>, ValidationProblem>> Add(
        NewReading request, [FromHeader(Name = LanguageHeader.Name)] string? language, ReadingService service, CancellationToken ct)
    {
        var result = await service.AddAsync(request, ct);
        return result.Reading is { } reading
            ? TypedResults.Created($"/readings/{reading.Id}", new SavedReading(reading.Id, Messages.Saved(LanguageHeader.Parse(language))))
            : TypedResults.ValidationProblem(result.Errors);
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> AttachPhoto(
        int id, IFormFile photo, ReadingService service, CancellationToken ct) =>
        await service.AttachPhotoAsync(id, photo, ct) switch
        {
            AttachPhotoResult.Attached => TypedResults.NoContent(),
            AttachPhotoResult.NotFound => TypedResults.NotFound(),
            _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["photo"] = [$"Must be a JPEG, PNG, HEIC or WebP image of at most {PhotoStore.MaxBytes / (1024 * 1024)} MB."],
            }),
        };
}
