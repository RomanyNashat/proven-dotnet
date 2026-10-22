namespace HomeReadings.Application;

/// <summary>The few texts the app shows, in the patient's language (the app's <c>x-language</c> header: ar or en).</summary>
/// <remarks>
/// The language is read from the header directly. Switching the thread culture to ar-SA for it would also
/// change how numbers and dates are parsed and formatted (Arabic separators, the Hijri calendar).
/// </remarks>
public static class Messages
{
    public static bool IsArabic(string? language) =>
        language is not null && language.Trim().StartsWith("ar", StringComparison.OrdinalIgnoreCase);

    public static string Saved(string? language) => IsArabic(language) ? "تم حفظ القراءة" : "Reading saved";

    public static string SomethingWentWrong(string? language) =>
        IsArabic(language) ? "حدث خطأ، يرجى المحاولة مرة أخرى" : "Something went wrong. Please try again.";
}
