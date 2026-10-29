using System.Globalization;
using System.Resources;

namespace LabResults.Application;

/// <summary>
/// SMS templates from <c>Resources/SmsText.resx</c>. The text never names the test: it goes through the
/// gateway and shows on lock screens. Read with the invariant culture, so no ICU is needed; the service
/// stores no patient language, so there is one (Arabic) template.
/// </summary>
public static class SmsText
{
    private static readonly ResourceManager Resources = new("LabResults.Resources.SmsText", typeof(SmsText).Assembly);

    /// <summary>"Your result is ready" with no detail about the result.</summary>
    public static string ResultReady => Resources.GetString(nameof(ResultReady), CultureInfo.InvariantCulture)
        ?? throw new InvalidOperationException("SmsText.resx has no ResultReady entry.");
}
