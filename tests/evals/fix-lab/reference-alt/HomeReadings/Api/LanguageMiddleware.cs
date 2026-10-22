using System.Globalization;

namespace HomeReadings.Api;

/// <summary>The app sends x-language: ar or en. It picks the texts (UI culture) only: numbers and dates the
/// service parses stay invariant, and ar-SA's separators and Hijri calendar never reach them.</summary>
public sealed class LanguageMiddleware(RequestDelegate next)
{
    private static readonly CultureInfo Arabic = new("ar-SA");
    private static readonly CultureInfo English = new("en-US");

    public Task InvokeAsync(HttpContext context)
    {
        var culture = context.Request.Headers["x-language"] == "ar" ? Arabic : English;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return next(context);
    }
}
