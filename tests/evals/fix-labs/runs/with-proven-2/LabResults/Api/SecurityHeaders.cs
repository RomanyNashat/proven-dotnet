namespace LabResults.Api;

public static class SecurityHeaders
{
    /// <summary>
    /// Headers for a JSON API, added in OnStarting rather than before next(): the exception handler clears
    /// response headers before writing its ProblemDetails, so headers added on the way in would be missing
    /// from exactly the error responses. Register first in the pipeline.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The same application.</returns>
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                var headers = http.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                if (!headers.ContainsKey("Cache-Control"))
                {
                    headers.CacheControl = "no-store";   // patient data must not sit in a shared cache
                }

                return Task.CompletedTask;
            });
            return next(http);
        });
}
