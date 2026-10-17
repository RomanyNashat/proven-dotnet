using System.Globalization;

namespace ClinicReminders.Application;

public static class GlobalizationGuard
{
    /// <summary>
    /// Fails the start-up with a clear message when the runtime has no ICU: SqlClient can't connect and the
    /// Arabic SMS can't be written without it.
    /// </summary>
    public static void RequireArabicCulture()
    {
        try { _ = CultureInfo.GetCultureInfo("ar-SA", predefinedOnly: true); }
        catch (CultureNotFoundException ex)
        {
            throw new InvalidOperationException(
                "This service needs ICU (the ar-SA culture for the SMS text, and SqlClient), but the runtime is in " +
                "globalization-invariant mode. Add icu-libs to the image and set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false.", ex);
        }
    }
}
