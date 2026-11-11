using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

/// <summary>
/// The SMS gateway (see VENDOR.md). Every POST sends a message and there is no idempotency key, so this client
/// must never get a retry policy: a retry after a timeout can text the patient twice.
/// </summary>
public sealed class SmsClient(HttpClient http)
{
    /// <exception cref="HttpRequestException">The gateway was unreachable or answered with an error status.</exception>
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
