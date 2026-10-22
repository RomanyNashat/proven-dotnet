using FluentValidation;
using HomeReadings.Application;
using HomeReadings.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace HomeReadings.Api;

public sealed record SavedReading(int Id, string Message);

public static class ReadingEndpoints
{
    // The photo plus the multipart framing around it.
    private const long MaxPhotoRequestBytes = PhotoStore.MaxBytes + (64 * 1024);

    public static void MapReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var readings = app.MapGroup("/readings").RequireAuthorization();

        readings.MapPost("/", async Task<Results<Created<SavedReading>, ValidationProblem>> (
            NewReading request, IValidator<NewReading> validator, ReadingService service, RequestLanguage language, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return TypedResults.ValidationProblem(validation.ToDictionary());
            }

            var result = await service.AddAsync(request, ct);
            if (result.Reading is not { } reading)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(NewReading.NationalId)] = [Messages.NationalIdMismatch(language.Value)],
                });
            }

            return TypedResults.Created($"/readings/{reading.Id}", new SavedReading(reading.Id, Messages.Saved(language.Value)));
        });

        readings.MapGet("/{id:int}", async Task<Results<Ok<ReadingView>, NotFound>> (int id, ReadingQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        readings.MapGet("/today", async (ReadingQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.TodayAsync(ct)));

        readings.MapGet("/latest", async Task<Results<Ok<ReadingView>, NotFound>> (ReadingQueries queries, CancellationToken ct) =>
            await queries.LatestAsync(ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        // Antiforgery is off because the API takes bearer tokens only; it has no cookies to forge.
        readings.MapPost("/{id:int}/photo", async Task<Results<NoContent, NotFound, ValidationProblem>> (
            int id, IFormFile photo, ReadingService service, RequestLanguage language, CancellationToken ct) =>
        {
            if (!PhotoStore.IsAcceptable(photo))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["photo"] = [Messages.InvalidPhoto(language.Value)],
                });
            }

            return await service.AttachPhotoAsync(id, photo, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
        })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxPhotoRequestBytes), new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxPhotoRequestBytes });
    }
}
