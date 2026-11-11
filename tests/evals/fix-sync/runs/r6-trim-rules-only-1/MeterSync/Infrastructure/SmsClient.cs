using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

/// <summary>The SMS gateway (see VENDOR.md). Every POST sends a message, so this client never retries.</summary>
public sealed class SmsClient(HttpClient http)
{
    /// <summary>Sends one text.</summary>
    /// <param name="to">The number, in international form.</param>
    /// <param name="text">The message.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="SmsRejectedException">The gateway answered with an error: nothing was sent.</exception>
    /// <exception cref="HttpRequestException">No answer: the message may or may not have been sent.</exception>
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new SmsRejectedException((int)response.StatusCode);
        }
    }
}
