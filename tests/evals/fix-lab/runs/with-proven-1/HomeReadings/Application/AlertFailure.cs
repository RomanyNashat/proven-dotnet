using System.Net;

namespace HomeReadings.Application;

/// <summary>Tells a care team refusal of one alert from the care team being unavailable.</summary>
public static class AlertFailure
{
    /// <summary>
    /// True when the care team answered that this alert is wrong (a 4xx other than 401, 403, 408 and 429):
    /// the next alerts can still go. Anything else means the care team is down or unreachable, and the run
    /// stops until the next tick rather than waiting on every alert in turn.
    /// </summary>
    /// <param name="exception">What the send threw.</param>
    /// <returns>Whether to carry on with the next alert.</returns>
    public static bool IsOnlyThisAlert(Exception exception) =>
        exception is HttpRequestException { StatusCode: { } status }
        && (int)status is >= 400 and < 500
        && status is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);

    /// <summary>What to record about a failure: the type and the status, never the message.</summary>
    /// <param name="exception">What the send threw.</param>
    /// <returns>A short description without patient data.</returns>
    public static string Describe(Exception exception) => exception is HttpRequestException { StatusCode: { } status }
        ? $"{exception.GetType().Name} {(int)status}"
        : exception.GetType().Name;
}
