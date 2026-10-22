namespace HomeReadings.Application;

/// <summary>
/// The few texts the app shows, looked up by language code. No CultureInfo is involved, so this works on an
/// image without ICU.
/// </summary>
public static class Messages
{
    /// <summary>The text shown when a reading is saved.</summary>
    /// <param name="language">A value from <see cref="LanguageHeader.Parse"/>.</param>
    /// <returns>The text in that language.</returns>
    public static string Saved(string language) => language == LanguageHeader.Arabic
        ? "تم حفظ القراءة"
        : "Reading saved";
}
