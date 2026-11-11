using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

/// <summary>The SMS gateway (see VENDOR.md). Every POST sends a message, so calls are never retried here.</summary>
public sealed class SmsClient(HttpClient http)
{
    /// <summary>Sends one SMS.</summary>
    /// <param name="to">The phone number, E.164.</param>
    /// <param name="text">The message.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="HttpRequestException">The gateway refused the message or couldn't be reached.</exception>
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
