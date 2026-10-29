using System.Globalization;
using System.Resources;

namespace LabResults.Application;

/// <summary>One SMS to send.</summary>
/// <param name="To">The number, in E.164 form.</param>
/// <param name="Text">The message.</param>
public sealed record Notification(string To, string Text)
{
    private static readonly ResourceManager Texts = new("LabResults.Resources.SmsTexts", typeof(Notification).Assembly);

    /// <summary>"Your result is ready", with no test name or value: the SMS must carry no health data.</summary>
    /// <param name="to">The number, in E.164 form.</param>
    /// <returns>The notification.</returns>
    public static Notification ResultReady(string to) =>
        new(to, Texts.GetString("ResultReady", CultureInfo.InvariantCulture)
                ?? throw new InvalidOperationException("SMS text 'ResultReady' is missing from the resources."));
}
