using System.Net.Http.Json;

namespace HomeReadings.Infrastructure;

/// <summary>
/// The care team's system. It has no idempotency key: every POST is a new alert to a nurse, so the
/// client never retries a POST (see Program) and the notifier makes sure only one instance sends.
/// </summary>
public sealed class CareTeamClient(HttpClient http)
{
    /// <param name="valueInMmolPerLitre">The care team's contract is in mmol/L.</param>
    public async Task SendAlertAsync(string nationalId, decimal valueInMmolPerLitre, DateTime collectedAt, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("alerts", new { nationalId, value = valueInMmolPerLitre, collectedAt }, ct);
        response.EnsureSuccessStatusCode();
    }
}
