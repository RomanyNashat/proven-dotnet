namespace MeterSync.Api;

public static class SecurityHeaders
{
    /// <summary>
    /// Headers for a JSON API, added in OnStarting so they are on every response, errors included (the
    /// exception handler clears headers added on the way in). Register first in the pipeline.
    /// </summary>
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
                    headers.CacheControl = "no-store";   // readings and meters are patient data
                }

                return Task.CompletedTask;
            });
            return next(http);
        });
}
