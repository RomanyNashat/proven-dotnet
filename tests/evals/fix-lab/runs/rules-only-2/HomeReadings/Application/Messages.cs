namespace HomeReadings.Application;

/// <summary>
/// The few texts the app shows, in the patient's language. The app sends <c>x-language: ar</c> or <c>en</c>.
/// The language only picks the text: it never changes how numbers or dates are parsed or formatted.
/// </summary>
public static class Messages
{
    public const string LanguageHeader = "x-language";

    public static bool IsArabic(string? language) =>
        language is not null && language.Trim().StartsWith("ar", StringComparison.OrdinalIgnoreCase);

    public static string Saved(string? language) => IsArabic(language) ? "تم حفظ القراءة" : "Reading saved";
}
