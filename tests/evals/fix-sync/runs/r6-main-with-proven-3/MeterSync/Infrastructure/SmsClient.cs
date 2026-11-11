using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

/// <summary>The SMS gateway (see VENDOR.md). Every POST sends a message; there is no idempotency key.</summary>
/// <remarks>Registered without a retry handler on purpose (Program.cs): a retry can send a text twice.</remarks>
public sealed class SmsClient(HttpClient http)
{
    /// <summary>Sends one text message.</summary>
    /// <exception cref="HttpRequestException">The gateway answered with an error status.</exception>
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
