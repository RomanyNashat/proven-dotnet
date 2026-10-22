using System.Net.Http.Json;

namespace HomeReadings.Infrastructure;

/// <summary>The care team's system. It has no idempotency key: every POST is a new alert to a nurse.</summary>
public sealed class CareTeamClient(HttpClient http)
{
    /// <summary>Sends one alert. The value is in mmol/L.</summary>
    /// <param name="nationalId">The patient's national ID.</param>
    /// <param name="value">The reading in mmol/L.</param>
    /// <param name="collectedAt">When the reading was taken, in UTC.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="HttpRequestException">The care team refused the alert.</exception>
    public async Task SendAlertAsync(string nationalId, decimal value, DateTime collectedAt, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("alerts", new { nationalId, value, collectedAt }, ct);
        response.EnsureSuccessStatusCode();
    }
}
