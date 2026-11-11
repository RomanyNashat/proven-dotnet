namespace MeterSync.Api;

/// <summary>Response headers for a JSON API that is never framed, sniffed or cached (responses carry health data).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>Adds the headers, then runs the rest of the pipeline.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The rest of the pipeline.</returns>
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        headers.CacheControl = "no-store";
        return next(context);
    }
}
