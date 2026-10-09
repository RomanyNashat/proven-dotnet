using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SkillSamples.Owasp;

public static class SecurityHeaders
{
    // Headers for a JSON API. They're added in OnStarting, not before next(): the exception handler clears
    // every response header before it writes its ProblemDetails, so headers added on the way in are missing
    // from exactly the error responses. Register this first in the pipeline.
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                var headers = http.Response.Headers;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";   // an API serves no pages
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
