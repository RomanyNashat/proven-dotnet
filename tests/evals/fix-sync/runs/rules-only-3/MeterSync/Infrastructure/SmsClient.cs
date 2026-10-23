using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

/// <summary>The SMS gateway (see VENDOR.md).</summary>
/// <remarks>
/// Every POST sends a message and the gateway has no idempotency key, so this client is registered
/// without a retry handler: a retried POST after a timeout texts the patient twice.
/// </remarks>
public sealed class SmsClient(HttpClient http)
{
    /// <summary>Sends one message.</summary>
    /// <param name="to">The number in the gateway's form, +9665... (see <see cref="SaudiMobile"/>).</param>
    /// <param name="text">The message.</param>
    /// <param name="ct">Cancels the send.</param>
    /// <exception cref="HttpRequestException">The gateway refused the message.</exception>
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
