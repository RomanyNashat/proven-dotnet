using System.Globalization;

namespace SkillSamples.Localization;

public static class GlobalizationGuard
{
    public static void RequireArabicCulture()
    {
        try { _ = CultureInfo.GetCultureInfo("ar-SA", predefinedOnly: true); }
        catch (CultureNotFoundException ex)
        {
            throw new InvalidOperationException(
                "This service localizes with resx and needs the ar-SA culture, but the runtime has no ICU " +
                "(globalization-invariant mode). Add ICU to the runtime image, or use the JSON pattern.", ex);
        }
    }
}
