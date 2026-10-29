using System.Net.Http.Json;
using LabResults.Application;

namespace LabResults.Infrastructure;

/// <summary>The SMS gateway (see LABS.md).</summary>
public sealed class SmsClient(HttpClient http) : ISmsSender
{
    /// <inheritdoc />
    public async Task SendAsync(string to, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("messages", new { to, text }, ct);
        response.EnsureSuccessStatusCode();
    }
}
