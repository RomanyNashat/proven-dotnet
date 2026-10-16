using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SkillSamples.Contracts;

public static class Deprecation
{
    /// <summary>
    /// Marks an endpoint as going away: <c>Deprecation</c> (RFC 9745, a Unix time), <c>Sunset</c> (RFC 8594, an
    /// HTTP date) and a link to what replaces it. Apps and their logs can see it long before it stops working.
    /// </summary>
    public static RouteHandlerBuilder Deprecated(this RouteHandlerBuilder endpoint, DateTimeOffset since, DateTimeOffset sunset, string replacement) =>
        endpoint.AddEndpointFilter(async (context, next) =>
        {
            var headers = context.HttpContext.Response.Headers;
            headers["Deprecation"] = "@" + since.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            headers["Sunset"] = sunset.ToString("R", CultureInfo.InvariantCulture);
            headers.Link = $"<{replacement}>; rel=\"deprecation\"";
            return await next(context);
        });
}
