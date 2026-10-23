using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

public sealed class SmsClient(HttpClient http)
{
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
