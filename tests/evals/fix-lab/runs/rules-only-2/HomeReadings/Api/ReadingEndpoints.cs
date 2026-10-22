using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HomeReadings.Api;

public sealed record ReadingSaved(int Id, string Message);

public static class ReadingEndpoints
{
    public static void MapReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var readings = app.MapGroup("/readings").RequireAuthorization();

        readings.MapPost("/", async Task<Results<Created<ReadingSaved>, Ok<ReadingSaved>, ValidationProblem>> (
            NewReading request,
            [FromHeader(Name = Messages.LanguageHeader)] string? language,
            ReadingService service,
            CancellationToken ct) =>
        {
            var result = await service.AddAsync(request, ct);
            return result.Outcome switch
            {
                AddReadingOutcome.Created => TypedResults.Created($"/readings/{result.Reading!.Id}", new ReadingSaved(result.Reading.Id, Messages.Saved(language))),
                AddReadingOutcome.AlreadySaved => TypedResults.Ok(new ReadingSaved(result.Reading!.Id, Messages.Saved(language))),
                _ => TypedResults.ValidationProblem(result.Errors),
            };
        });

        readings.MapGet("/{id:int}", async Task<Results<Ok<ReadingView>, NotFound>> (int id, ReadingQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        readings.MapGet("/today", async (ReadingQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.TodayAsync(ct)));

        readings.MapGet("/latest", async Task<Results<Ok<ReadingView>, NotFound>> (ReadingQueries queries, CancellationToken ct) =>
            await queries.LatestAsync(ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        // Bearer tokens, no cookies: a cross-site form cannot carry the token, so antiforgery adds nothing here.
        readings.MapPost("/{id:int}/photo", async Task<Results<NoContent, NotFound, ValidationProblem>> (int id, IFormFile photo, ReadingService service, CancellationToken ct) =>
                await service.AttachPhotoAsync(id, photo, ct) switch
                {
                    AttachPhotoOutcome.Attached => TypedResults.NoContent(),
                    AttachPhotoOutcome.NotFound => TypedResults.NotFound(),
                    _ => TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["photo"] = [$"Must be a JPEG, PNG or HEIC image of at most {PhotoStore.MaxBytes / (1024 * 1024)} MB."],
                    }),
                })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PhotoStore.MaxBytes + (64 * 1024)))
            .WithFormOptions(multipartBodyLengthLimit: PhotoStore.MaxBytes + (64 * 1024));
    }
}
