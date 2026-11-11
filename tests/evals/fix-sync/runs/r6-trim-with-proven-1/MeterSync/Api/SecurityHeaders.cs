namespace MeterSync.Api;

public static class SecurityHeaders
{
    // A JSON API: nothing is rendered, framed or scripted from its responses.
    private const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>Adds nosniff, DENY framing and a deny-all CSP to every response, error responses included.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The application.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            // OnStarting, so the headers survive the exception handler clearing the response.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                return Task.CompletedTask;
            });
            return next(context);
        });
}
