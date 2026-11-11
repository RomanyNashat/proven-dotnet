using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

/// <summary>
/// The SMS gateway (see VENDOR.md). Every POST sends a message and there is no idempotency key, so this
/// client must never be given a retry handler.
/// </summary>
public sealed class SmsClient(HttpClient http)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
