using FluentValidation;
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

        readings.MapPost("/", async Task<Results<Created<SavedReading>, ValidationProblem>> (
            NewReading request, IValidator<NewReading> validator, ReadingService service, HttpRequest http, CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(request, ct);
            if (!validation.IsValid)
            {
                return TypedResults.ValidationProblem(validation.ToDictionary());
            }

            var result = await service.AddAsync(request, ct);
            if (result.Errors is { } errors)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var language = LanguageHeader.Parse(http.Headers[LanguageHeader.Name]);
            return TypedResults.Created($"/readings/{result.Reading!.Id}", new SavedReading(result.Reading.Id, Messages.Saved(language)));
        });

        readings.MapGet("/{id:int}", async Task<Results<Ok<ReadingView>, NotFound>> (int id, ReadingQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        readings.MapGet("/today", async (ReadingQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.TodayAsync(ct)));

        readings.MapGet("/latest", async Task<Results<Ok<ReadingView>, NotFound>> (ReadingQueries queries, CancellationToken ct) =>
            await queries.LatestAsync(ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        // Bearer tokens only, no cookies, so there is no cross-site request to forge.
        readings.MapPost("/{id:int}/photo", async Task<Results<NoContent, NotFound, ValidationProblem>> (
                int id, IFormFile photo, ReadingService service, CancellationToken ct) =>
            {
                if (PhotoStore.Check(photo) is { } problem)
                {
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["photo"] = [problem] });
                }

                return await service.AttachPhotoAsync(id, photo, ct) ? TypedResults.NoContent() : TypedResults.NotFound();
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(PhotoStore.MaxBytes + (64 * 1024)));
    }
}
