namespace HomeReadings.Api;

/// <summary>
/// The app sends x-language: ar or en. It picks the text only: the formatting culture stays invariant
/// (ar-SA would parse numbers with "٫" and dates with the Hijri calendar), and no CultureInfo is created,
/// so this also works on an image without ICU.
/// </summary>
public static class AppLanguage
{
    public const string Header = "x-language";

    public static bool IsArabic(HttpRequest request) =>
        request.Headers[Header].ToString().Trim().StartsWith("ar", StringComparison.OrdinalIgnoreCase);
}
