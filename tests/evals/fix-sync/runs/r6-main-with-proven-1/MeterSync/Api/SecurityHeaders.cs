namespace MeterSync.Api;

/// <summary>Adds the browser security headers to every response, including errors and CORS preflights.</summary>
public static class SecurityHeaders
{
    /// <summary>Registers the middleware. Put it before CORS so short-circuited preflights get the headers too.</summary>
    /// <param name="app">The pipeline.</param>
    /// <returns>The same pipeline.</returns>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            // On starting, so headers survive the exception handler clearing the response.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                return Task.CompletedTask;
            });
            return next(context);
        });
}
