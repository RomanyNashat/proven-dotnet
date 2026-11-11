namespace MeterSync.Infrastructure;

/// <summary>The SMS gateway answered with an error status, so the message was not sent.</summary>
public sealed class SmsRejectedException(int statusCode)
    : Exception($"The SMS gateway answered {statusCode}.")
{
    /// <summary>The HTTP status the gateway answered with.</summary>
    public int StatusCode { get; } = statusCode;
}
