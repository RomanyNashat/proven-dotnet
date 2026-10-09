using Microsoft.AspNetCore.Http.HttpResults;

namespace Activity;

public static class ActivityEndpoints
{
    /// <summary>The caller's step count for today (Riyadh day, UTC+3).</summary>
    public static async Task<Ok<DayTotal>> Today(ICurrentUser user, ActivityService service, CancellationToken ct) =>
        TypedResults.Ok(await service.TodayAsync(user.Id, ct));

    /// <summary>Uploads step counts from the phone, one entry per day.</summary>
    public static async Task<Results<NoContent, ValidationProblem>> Sync(SyncRequest request, ICurrentUser user, ActivityService service, CancellationToken ct)
    {
        var errors = service.Validate(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await service.SyncAsync(user.Id, request, ct);   // upserts by (user, day), then publishes "steps-synced"
        return TypedResults.NoContent();
    }

    /// <summary>Daily totals between two dates, at most 90 days.</summary>
    public static async Task<Results<Ok<IReadOnlyList<DayTotal>>, ValidationProblem>> History(
        DateOnly from, DateOnly to, ICurrentUser user, ActivityService service, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 90)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["at most 90 days after from"] });
        }

        return TypedResults.Ok(await service.HistoryAsync(user.Id, from, to, ct));
    }
}
