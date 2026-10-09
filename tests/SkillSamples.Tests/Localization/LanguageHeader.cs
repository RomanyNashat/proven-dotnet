namespace SkillSamples.Localization;

public static class LanguageHeader
{
    public const string Name = "x-language";
    public const string Arabic = "ar";
    public const string English = "en";

    // "ar", "ar-SA", "AR" → ar. Anything else, or no header → en.
    public static string Parse(string? value) =>
        value is not null && value.Trim().StartsWith(Arabic, StringComparison.OrdinalIgnoreCase) ? Arabic : English;
}
