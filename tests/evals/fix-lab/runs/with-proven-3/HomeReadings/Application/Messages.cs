namespace HomeReadings.Application;

/// <summary>
/// The few texts the app shows, looked up by language code. No CultureInfo: the image has no ICU, and
/// creating ar-SA (or en-US) there throws.
/// </summary>
public static class Messages
{
    /// <summary>Shown after a reading is saved.</summary>
    /// <param name="language">A code from <see cref="LanguageHeader.Parse"/>.</param>
    /// <returns>The text in that language.</returns>
    public static string Saved(string language) => language == LanguageHeader.Arabic
        ? "تم حفظ القراءة"
        : "Reading saved";
}
