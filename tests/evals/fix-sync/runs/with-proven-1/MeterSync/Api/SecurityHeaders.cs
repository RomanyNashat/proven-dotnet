namespace MeterSync.Api;

public static class SecurityHeaders
{
    // Added in OnStarting, not before next(): the exception handler clears the response headers before it
    // writes its ProblemDetails, so headers added on the way in would be missing from exactly the errors.
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                var headers = http.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";   // an API serves no pages
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                if (!headers.ContainsKey("Cache-Control"))
                {
                    headers.CacheControl = "no-store";   // glucose data must not sit in a shared cache
                }

                return Task.CompletedTask;
            });
            return next(http);
        });
}
