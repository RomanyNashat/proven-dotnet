namespace HomeReadings.Application;

/// <summary>The app sends <c>x-language: ar</c> or <c>en</c>. It picks the text, never the formatting culture.</summary>
public static class LanguageHeader
{
    public const string Name = "x-language";
    public const string Arabic = "ar";
    public const string English = "en";

    /// <summary>"ar", "ar-SA", "AR" give Arabic. Anything else, or no header, gives English.</summary>
    /// <param name="value">The header's value.</param>
    /// <returns><see cref="Arabic"/> or <see cref="English"/>.</returns>
    public static string Parse(string? value) =>
        value is not null && value.Trim().StartsWith(Arabic, StringComparison.OrdinalIgnoreCase) ? Arabic : English;
}
