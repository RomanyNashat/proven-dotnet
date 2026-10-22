using System.Globalization;

namespace HomeReadings.Api;

/// <summary>The app sends x-language: ar or en. Only the UI culture (message texts) follows it.</summary>
public sealed class LanguageMiddleware(RequestDelegate next)
{
    private static readonly CultureInfo Arabic = new("ar-SA");
    private static readonly CultureInfo English = new("en-US");

    public Task InvokeAsync(HttpContext context)
    {
        // CurrentCulture stays as is: parsing and formatting must not change with the app's language.
        CultureInfo.CurrentUICulture = string.Equals(context.Request.Headers["x-language"], "ar", StringComparison.OrdinalIgnoreCase)
            ? Arabic
            : English;
        return next(context);
    }
}
