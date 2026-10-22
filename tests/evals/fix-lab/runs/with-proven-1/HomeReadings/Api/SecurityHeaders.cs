namespace HomeReadings.Api;

/// <summary>Every response carries patient data or could: never cached, never sniffed, never framed.</summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) => app.Use((context, next) =>
    {
        var headers = context.Response.Headers;
        headers.CacheControl = "no-store";
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        return next(context);
    });
}
