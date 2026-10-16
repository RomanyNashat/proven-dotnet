using System.Globalization;

namespace ClinicReminders.Api;

public static class GlobalizationGuard
{
    // Without ICU (globalization-invariant mode) SqlClient can't connect and ar-SA, which the SMS text
    // uses, doesn't exist. Fail at start-up with the reason, not at the first query or the 09:00 run.
    public static void RequireArabicCulture()
    {
        try { _ = CultureInfo.GetCultureInfo("ar-SA", predefinedOnly: true); }
        catch (CultureNotFoundException ex)
        {
            throw new InvalidOperationException(
                "This service needs ICU with the ar-SA culture (Arabic SMS text, and SqlClient), but the runtime has " +
                "none (globalization-invariant mode, or Alpine's icu-libs without icu-data-full). Add ICU to the " +
                "runtime image and set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false.", ex);
        }
    }
}
