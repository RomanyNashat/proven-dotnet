using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HomeReadings.Api;

public sealed record SavedReading(int Id, string Message);

public static class ReadingEndpoints
{
    public static void MapReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var readings = app.MapGroup("/readings").RequireAuthorization();

        readings.MapPost("/", async Task<Results<Created<SavedReading>, Ok<SavedReading>, ValidationProblem>> (
            NewReading request, HttpRequest http, ReadingService service, CancellationToken ct) =>
        {
            var (valid, errors) = NewReadingValidator.Validate(request);
            if (valid is null)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var result = await service.AddAsync(valid, ct);
            var body = new SavedReading(result.Id, Messages.Saved(AppLanguage.IsArabic(http)));
            return result.Created
                ? TypedResults.Created($"/readings/{result.Id}", body)
                : TypedResults.Ok(body);
        });

        readings.MapGet("/{id:int}", async Task<Results<Ok<ReadingView>, NotFound>> (int id, ReadingQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        readings.MapGet("/today", async (ReadingQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.TodayAsync(ct)));

        readings.MapGet("/latest", async Task<Results<Ok<ReadingView>, NotFound>> (ReadingQueries queries, CancellationToken ct) =>
            await queries.LatestAsync(ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        // Bearer-token API with no cookies, so there is no ambient credential for a forged form to use.
        readings.MapPost("/{id:int}/photo", async Task<Results<NoContent, NotFound, ValidationProblem>> (
            int id, IFormFile photo, ReadingService service, CancellationToken ct) =>
            {
                if (photo.Length is 0 or > PhotoStore.MaxBytes)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["photo"] = [$"Must be a picture of up to {PhotoStore.MaxBytes / (1024 * 1024)} MB."],
                    });
                }

                var extension = await PhotoStore.DetectExtensionAsync(photo, ct);
                if (extension is null)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["photo"] = ["Must be a JPEG or PNG picture."],
                    });
                }

                return await service.AttachPhotoAsync(id, photo, extension, ct) == AttachPhotoResult.Attached
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PhotoStore.MaxBytes + (64 * 1024)));
    }
}
