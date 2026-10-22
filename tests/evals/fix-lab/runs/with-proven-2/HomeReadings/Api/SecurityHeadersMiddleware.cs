namespace HomeReadings.Api;

/// <summary>
/// Headers for a JSON API that returns health data. Added as the response starts, so a response the
/// exception handler rewrote carries them too.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers.CacheControl = "no-store";   // readings are PHI: no proxy or device cache keeps them
            return Task.CompletedTask;
        });
        return next(context);
    }
}
