using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;

namespace ClinicReminders.Infrastructure;

public sealed record SmsRequest(string To, string Text);

/// <summary>What is known about a send. The provider has no idempotency key, so only <see cref="NotSent"/> is safe to retry.</summary>
public enum SmsOutcome
{
    /// <summary>The provider accepted the message.</summary>
    Sent,

    /// <summary>The request never reached the provider, or it was throttled (429). Safe to retry.</summary>
    NotSent,

    /// <summary>The provider refused it (4xx other than 429). Retrying won't help.</summary>
    Rejected,

    /// <summary>It may have been delivered (timeout, 5xx, connection lost mid-request). Not safe to retry.</summary>
    Unknown,
}

public sealed record SmsResult(SmsOutcome Outcome, int? StatusCode = null, Exception? Error = null);

public sealed class SmsOptions
{
    /// <summary>The provider's API base, e.g. https://sms.example.net/api/v2/.</summary>
    [Required]
    public Uri? BaseUrl { get; init; }

    /// <summary>How long one send may take before its outcome is treated as unknown.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:02:00")]
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public sealed class SmsClient(HttpClient http)
{
    /// <summary>Sends one SMS. Never retries: a retry after an ambiguous failure would send the patient a duplicate.</summary>
    /// <param name="to">The mobile number.</param>
    /// <param name="text">The message.</param>
    /// <param name="ct">Cancellation token. Cancelling it propagates <see cref="OperationCanceledException"/>.</param>
    /// <returns>What is known about the send.</returns>
    public async Task<SmsResult> SendAsync(string to, string text, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync("messages", new SmsRequest(to, text), ct);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError)
        {
            // No connection was made, so nothing reached the provider.
            return new SmsResult(SmsOutcome.NotSent, Error: ex);
        }
        catch (HttpRequestException ex)
        {
            return new SmsResult(SmsOutcome.Unknown, Error: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout: the provider may already have the message.
            return new SmsResult(SmsOutcome.Unknown, Error: ex);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return new SmsResult(SmsOutcome.Sent, status);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new SmsResult(SmsOutcome.NotSent, status);
            }

            return status is >= 400 and < 500
                ? new SmsResult(SmsOutcome.Rejected, status)
                : new SmsResult(SmsOutcome.Unknown, status);
        }
    }
}
