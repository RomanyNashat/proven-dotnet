using System.Net.Http.Json;

namespace ClinicReminders.Infrastructure;

public sealed record SmsRequest(string To, string Text);

/// <summary>
/// The SMS provider. It takes no idempotency key, so a POST is never retried here, and the result says
/// whether the message certainly didn't go out (safe to try again) or might have (never try again).
/// </summary>
public sealed class SmsClient(HttpClient http)
{
    // A relative path with no leading '/': the base address keeps its "/api/v2/".
    private const string MessagesPath = "messages";

    public async Task<SmsResult> SendAsync(string to, string text, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(MessagesPath, new SmsRequest(to, text), ct);
            return response.IsSuccessStatusCode
                ? SmsResult.Sent
                : new SmsResult(SmsDelivery.NotSent, (int)response.StatusCode);
        }
        catch (HttpRequestException ex) when (NeverLeft(ex.HttpRequestError))
        {
            return new SmsResult(SmsDelivery.NotSent, Failure: ex.HttpRequestError.ToString());
        }
        catch (HttpRequestException ex)
        {
            return new SmsResult(SmsDelivery.Unknown, Failure: ex.HttpRequestError.ToString());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout, not the caller: the request may have been delivered.
            return new SmsResult(SmsDelivery.Unknown, Failure: nameof(TimeoutException));
        }
    }

    // Failures before the request was written: the provider never saw it.
    private static bool NeverLeft(HttpRequestError error) => error is
        HttpRequestError.NameResolutionError or
        HttpRequestError.ConnectionError or
        HttpRequestError.SecureConnectionError or
        HttpRequestError.ProxyTunnelError;
}
