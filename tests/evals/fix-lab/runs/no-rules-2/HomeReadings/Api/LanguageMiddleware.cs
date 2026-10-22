using System.Globalization;

namespace HomeReadings.Api;

/// <summary>The app sends x-language: ar or en. Only the UI culture follows it: it picks texts, and
/// must not change how numbers and dates are parsed or formatted (ar-SA uses another calendar).</summary>
public sealed class LanguageMiddleware(RequestDelegate next)
{
    private static readonly CultureInfo Arabic = new("ar-SA");
    private static readonly CultureInfo English = new("en-US");

    public Task InvokeAsync(HttpContext context)
    {
        CultureInfo.CurrentUICulture = context.Request.Headers["x-language"] == "ar" ? Arabic : English;
        return next(context);
    }
}
