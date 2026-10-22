namespace HomeReadings.Api;

/// <summary>
/// The app sends x-language: ar or en. It picks the text only: it never becomes the formatting culture,
/// whose calendar and digits for ar-SA depend on the machine's ICU (and which doesn't exist without ICU).
/// </summary>
public sealed class RequestLanguage(IHttpContextAccessor accessor)
{
    public const string Header = "x-language";
    public const string Arabic = "ar";
    public const string English = "en";

    public string Value => Parse(accessor.HttpContext?.Request.Headers[Header]);

    /// <summary>"ar", "ar-SA", "AR" give Arabic; anything else, or no header, English.</summary>
    public static string Parse(string? value) =>
        value is not null && value.Trim().StartsWith(Arabic, StringComparison.OrdinalIgnoreCase) ? Arabic : English;
}
