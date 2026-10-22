using System.Net.Http.Json;
using Polly.CircuitBreaker;

namespace HomeReadings.Infrastructure;

public enum AlertDelivery
{
    /// <summary>The care team accepted the alert.</summary>
    Delivered,

    /// <summary>The care team certainly did not take it (it answered with an error, or no connection was made). Safe to send again.</summary>
    NotDelivered,

    /// <summary>The request may have reached the care team (timeout, dropped connection). Sending again could alert a nurse twice.</summary>
    Unknown,
}

/// <summary>
/// The care team's system. It has no idempotency key: every POST is a new alert to a nurse.
/// So the HTTP pipeline never retries the POST, and this client says whether a failed send could have arrived.
/// </summary>
public sealed class CareTeamClient(HttpClient http, ILogger<CareTeamClient> logger)
{
    /// <param name="valueInMmolPerL">The reading in mmol/L, whatever unit the meter showed.</param>
    public async Task<AlertDelivery> SendAlertAsync(string nationalId, decimal valueInMmolPerL, DateTime collectedAt, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("alerts", new { nationalId, value = valueInMmolPerL, collectedAt }, ct);
            if (response.IsSuccessStatusCode)
            {
                return AlertDelivery.Delivered;
            }

            logger.LogWarning("The care team refused an alert with {StatusCode}", (int)response.StatusCode);
            return AlertDelivery.NotDelivered;
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError is HttpRequestError.NameResolutionError
            or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError)
        {
            logger.LogWarning(ex, "Could not connect to the care team");
            return AlertDelivery.NotDelivered;
        }
        catch (BrokenCircuitException ex)
        {
            logger.LogWarning(ex, "The care team circuit is open; the alert was not sent");
            return AlertDelivery.NotDelivered;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or Polly.Timeout.TimeoutRejectedException)
        {
            logger.LogError(ex, "Sending an alert to the care team ended without an answer; it may have arrived");
            return AlertDelivery.Unknown;
        }
    }
}
