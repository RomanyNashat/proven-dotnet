namespace ClinicReminders.Infrastructure;

public enum SmsDelivery
{
    /// <summary>The provider accepted the message.</summary>
    Sent,

    /// <summary>The message certainly didn't go out: the provider refused it, or no connection was made.</summary>
    NotSent,

    /// <summary>The request may have reached the provider, but no answer came back (a timeout, a cut response).</summary>
    Unknown,
}

/// <param name="Delivery">What is known about the message.</param>
/// <param name="StatusCode">The provider's status when it answered with an error.</param>
/// <param name="Failure">The failure's type name. Never its message: it can echo the request.</param>
public sealed record SmsResult(SmsDelivery Delivery, int? StatusCode = null, string? Failure = null)
{
    public static readonly SmsResult Sent = new(SmsDelivery.Sent);
}
