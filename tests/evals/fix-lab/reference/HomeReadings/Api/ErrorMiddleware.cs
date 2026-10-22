namespace HomeReadings.Api;

/// <summary>Turns an unhandled error into a ProblemDetails that says nothing about the inside.</summary>
public sealed class ErrorMiddleware(RequestDelegate next, ILogger<ErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            // The type only: a database error's message can carry the values it rejected.
            logger.LogError("Request failed: {Error}", ex.GetType().Name);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await TypedResults.Problem(title: "Something went wrong", statusCode: 500).ExecuteAsync(context);
        }
    }
}
