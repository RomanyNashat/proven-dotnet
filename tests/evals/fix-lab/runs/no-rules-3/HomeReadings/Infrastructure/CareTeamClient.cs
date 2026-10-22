using System.Net.Http.Json;

namespace HomeReadings.Infrastructure;

/// <summary>The care team's system. It has no idempotency key: every POST is a new alert to a nurse.</summary>
public sealed class CareTeamClient(HttpClient http)
{
    public async Task SendAlertAsync(string nationalId, decimal value, DateTime collectedAt, CancellationToken ct)
    {
        var response = await http.PostAsJsonAsync("alerts", new { nationalId, value, collectedAt }, ct);
        response.EnsureSuccessStatusCode();
    }
}
