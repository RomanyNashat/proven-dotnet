using System.Globalization;

namespace HomeReadings.Api;

/// <summary>The app sends x-language: ar or en. Only the UI culture follows it (for messages); parsing and formatting stay invariant.</summary>
public sealed class LanguageMiddleware(RequestDelegate next)
{
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    public Task InvokeAsync(HttpContext context)
    {
        var language = context.Request.Headers["x-language"].ToString();
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = language.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? Arabic : English;
        return next(context);
    }
}
