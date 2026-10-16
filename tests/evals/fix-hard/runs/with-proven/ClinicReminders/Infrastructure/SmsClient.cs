using System.Net.Http.Json;

namespace ClinicReminders.Infrastructure;

public sealed record SmsRequest(string To, string Text);

public sealed class SmsClient(HttpClient http)
{
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new SmsRequest(to, text), ct);
        response.EnsureSuccessStatusCode();
    }
}
