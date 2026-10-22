namespace HomeReadings.Api;

public static class SecurityHeaders
{
    /// <summary>Every response carries health data or answers about it: no caching anywhere, no sniffing.</summary>
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) => app.Use((http, next) =>
    {
        http.Response.OnStarting(() =>
        {
            var headers = http.Response.Headers;
            headers.CacheControl = "no-store";
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            return Task.CompletedTask;
        });
        return next(http);
    });
}
