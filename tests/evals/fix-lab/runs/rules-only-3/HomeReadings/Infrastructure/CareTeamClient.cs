using System.Net.Http.Json;

namespace HomeReadings.Infrastructure;

/// <summary>The care team's system. It has no idempotency key: every POST is a new alert to a nurse.</summary>
/// <remarks>
/// So this client is registered without retries for POST (see Program), and the caller decides what a
/// failure means: see <see cref="Application.ReadingNotifier"/>.
/// </remarks>
public sealed class CareTeamClient(HttpClient http)
{
    /// <param name="valueInMmolPerLitre">The reading in mmol/L, the unit the care team works in.</param>
    public async Task SendAlertAsync(string nationalId, decimal valueInMmolPerLitre, DateTime collectedAt, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("alerts", new { nationalId, value = valueInMmolPerLitre, collectedAt }, ct);
        response.EnsureSuccessStatusCode();
    }
}
