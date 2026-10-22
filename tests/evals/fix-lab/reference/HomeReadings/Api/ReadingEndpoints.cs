using HomeReadings.Application;

namespace HomeReadings.Api;

public static class ReadingEndpoints
{
    public static void MapReadingEndpoints(this IEndpointRouteBuilder app)
    {
        var readings = app.MapGroup("/readings").RequireAuthorization();

        readings.MapPost("/", async Task<IResult> (NewReading request, ReadingService service, CancellationToken ct) =>
        {
            if (ReadingService.Problem(request) is { } field)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = ["Not valid."] });
            }

            var reading = await service.AddAsync(request, ct);
            return TypedResults.Created($"/readings/{reading.Id}", new { id = reading.Id, message = Messages.Saved });
        });

        readings.MapGet("/{id:int}", async Task<IResult> (int id, ReadingQueries queries, CancellationToken ct) =>
            await queries.GetAsync(id, ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        readings.MapGet("/today", async (ReadingQueries queries, CancellationToken ct) =>
            TypedResults.Ok(await queries.TodayAsync(ct)));

        readings.MapGet("/latest", async Task<IResult> (ReadingQueries queries, CancellationToken ct) =>
            await queries.LatestAsync(ct) is { } reading ? TypedResults.Ok(reading) : TypedResults.NotFound());

        readings.MapPost("/{id:int}/photo", async Task<IResult> (int id, IFormFile photo, ReadingService service, CancellationToken ct) =>
            await service.AttachPhotoAsync(id, photo, ct) ? TypedResults.NoContent() : TypedResults.NotFound())
            .DisableAntiforgery();
    }
}
