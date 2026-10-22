using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace HomeReadings.Api;

public static class ReadingEndpoints
{
    public static void MapReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var readings = app.MapGroup("/readings").RequireAuthorization();

        readings.MapPost("/", async Task<IResult> (NewReading request, [FromHeader(Name = "x-language")] string? language,
            ReadingService service, CancellationToken ct) =>
        {
            var result = await service.AddAsync(request, ct);
            return result.Reading is { } reading
                ? TypedResults.Created($"/readings/{reading.Id}", new { id = reading.Id, message = Messages.Saved(language) })
                : TypedResults.ValidationProblem(result.Errors);
        });

        readings.MapGet("/{id:int}", async Task<IResult> (int id, ReadingQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        readings.MapGet("/today", async (ReadingQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.TodayAsync(ct)));

        readings.MapGet("/latest", async Task<IResult> (ReadingQueries queries, CancellationToken ct) =>
            await queries.LatestAsync(ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        // Antiforgery protects cookie-authenticated forms; this API takes bearer tokens only, so a
        // cross-site form cannot carry the patient's credentials.
        readings.MapPost("/{id:int}/photo", async Task<IResult> (int id, IFormFile photo, ReadingService service, CancellationToken ct) =>
                await service.AttachPhotoAsync(id, photo, ct) switch
                {
                    PhotoResult.Saved => TypedResults.NoContent(),
                    PhotoResult.NotAnImage => TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["photo"] = [$"Must be a JPEG or PNG image of at most {PhotoStore.MaxBytes / (1024 * 1024)} MB."],
                    }),
                    _ => TypedResults.NotFound(),
                })
            .DisableAntiforgery()
            .WithFormOptions(multipartBodyLengthLimit: PhotoStore.MaxBytes + (64 * 1024));
    }
}
