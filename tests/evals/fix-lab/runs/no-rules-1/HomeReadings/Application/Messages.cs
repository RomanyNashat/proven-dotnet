using System.Globalization;

namespace HomeReadings.Application;

/// <summary>The few texts the app shows, in the patient's language.</summary>
public static class Messages
{
    public static string Saved => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar"
        ? "تم حفظ القراءة"
        : "Reading saved";
}
