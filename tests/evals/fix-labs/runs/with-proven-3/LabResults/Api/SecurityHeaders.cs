namespace LabResults.Api;

public static class SecurityHeaders
{
    /// <summary>
    /// Headers for a JSON API, added in OnStarting: the exception handler clears headers added on the way in,
    /// so they would be missing from exactly the error responses (owasp-aspnetcore §1). Register first.
    /// </summary>
    /// <param name="app">The pipeline.</param>
    /// <returns>The same pipeline.</returns>
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                var headers = http.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                if (!headers.ContainsKey("Cache-Control"))
                {
                    headers["Cache-Control"] = "no-store";   // patient data must not sit in a shared cache
                }

                return Task.CompletedTask;
            });
            return next(http);
        });
}
