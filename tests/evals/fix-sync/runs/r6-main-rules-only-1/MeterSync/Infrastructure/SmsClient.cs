using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

/// <summary>The SMS gateway (see VENDOR.md). Every POST sends a message, so this client never retries.</summary>
public sealed class SmsClient(HttpClient http)
{
    /// <summary>Sends one SMS.</summary>
    /// <param name="to">The number in E.164 form.</param>
    /// <param name="text">The message.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="HttpRequestException">The gateway refused the message.</exception>
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
