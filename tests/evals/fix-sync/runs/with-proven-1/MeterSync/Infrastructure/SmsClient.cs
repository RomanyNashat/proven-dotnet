using System.Net.Http.Json;
using MeterSync.Application;

namespace MeterSync.Infrastructure;

public sealed class SmsClient(HttpClient http) : ISmsSender
{
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
