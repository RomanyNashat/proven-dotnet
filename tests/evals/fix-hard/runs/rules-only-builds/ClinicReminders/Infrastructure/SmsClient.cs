using System.Net;
using System.Net.Http.Json;

namespace ClinicReminders.Infrastructure;

public sealed record SmsRequest(string To, string Text);

/// <summary>What happened to one SMS, as far as we can tell.</summary>
public enum SmsOutcome
{
    Sent,

    /// <summary>The provider certainly did not take the message (no connection, 429, 503). Safe to try again.</summary>
    NotSent,

    /// <summary>The provider refused the message. Trying again would fail the same way.</summary>
    Rejected,

    /// <summary>The provider may have sent it. The provider has no idempotency key, so trying again risks a duplicate.</summary>
    Unknown,
}

/// <remarks>
/// Deliberately no retry handler on this client: a POST to a provider without an idempotency key is not safe to
/// repeat. A request is made once, and the outcome decides whether a later job run may try again.
/// </remarks>
public sealed class SmsClient(HttpClient http)
{
    public async Task<SmsOutcome> SendAsync(string to, string text, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("messages", new SmsRequest(to, text), ct);
            return Classify(response.StatusCode);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.NameResolutionError
                                                  or HttpRequestError.ConnectionError
                                                  or HttpRequestError.SecureConnectionError)
        {
            return SmsOutcome.NotSent;
        }
        catch (HttpRequestException)
        {
            return SmsOutcome.Unknown;
        }
        catch (OperationCanceledException)
        {
            // The client timeout, or shutdown, while the request was in flight.
            return SmsOutcome.Unknown;
        }
    }

    private static SmsOutcome Classify(HttpStatusCode status) => (int)status switch
    {
        >= 200 and < 300 => SmsOutcome.Sent,
        // Both say the request was not processed.
        429 or 503 => SmsOutcome.NotSent,
        408 => SmsOutcome.Unknown,
        >= 400 and < 500 => SmsOutcome.Rejected,
        _ => SmsOutcome.Unknown,
    };
}
