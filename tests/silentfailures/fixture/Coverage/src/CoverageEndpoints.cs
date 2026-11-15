namespace Coverage;

public static class CoverageEndpoints
{
    public static async Task<IResult> Get(int patientId, InsurerClient insurer, CancellationToken ct)
    {
        try
        {
            return TypedResults.Ok(await insurer.TryCoverageAsync(patientId, ct));
        }
        catch (InsurerUnavailableException)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The insurer can't be reached.");
        }
    }

    public static async Task WarmAsync(ICoverageCache cache, CancellationToken ct)
    {
        // A cold cache is fine: the next request reads the insurer.
        try { await cache.WarmAsync(ct); }
        catch (TimeoutException ex) { throw new CacheWarmFailedException(ex); }
    }
}
