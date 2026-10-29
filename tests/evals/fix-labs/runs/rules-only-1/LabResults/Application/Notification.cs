namespace LabResults.Application;

/// <summary>An SMS to send through the gateway.</summary>
/// <param name="To">E.164 phone number.</param>
/// <param name="Text">Message body.</param>
public sealed record Notification(string To, string Text)
{
    /// <summary>
    /// Tells the patient a result is ready. It deliberately does not name the test: an SMS shows on a
    /// lock screen and may reach the wrong person, so it carries no health information.
    /// </summary>
    /// <param name="to">E.164 phone number.</param>
    /// <returns>The notification.</returns>
    public static Notification ResultReady(string to) =>
        new(to, "نتيجة تحليل جديدة جاهزة. يمكنك الاطلاع عليها في التطبيق.");
}
