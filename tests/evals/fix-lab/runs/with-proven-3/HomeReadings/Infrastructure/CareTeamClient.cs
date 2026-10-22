using System.Net;
using System.Net.Http.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace HomeReadings.Infrastructure;

/// <summary>The care team's system. It has no idempotency key: every POST is a new alert to a nurse.</summary>
public sealed class CareTeamClient(HttpClient http)
{
    public async Task<AlertDelivery> SendAlertAsync(CareTeamAlertMessage alert, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("alerts", alert, ct);
            return response.IsSuccessStatusCode
                ? new AlertDelivery(AlertDeliveryOutcome.Delivered, null)
                : new AlertDelivery(IsWorthRetrying(response.StatusCode) ? AlertDeliveryOutcome.Failed : AlertDeliveryOutcome.Rejected,
                    $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) when (NeverSent(ex))
        {
            return new AlertDelivery(AlertDeliveryOutcome.Failed, ex.GetType().Name);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return new AlertDelivery(AlertDeliveryOutcome.Unknown, ex.GetType().Name);
        }
    }

    private static bool IsWorthRetrying(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;

    // Failures before the request could leave this process: no connection, an open circuit, or an
    // HttpRequestException with no transport error behind it (thrown before any I/O). A 5xx counts as
    // "not accepted" and is retried by the notifier; a timeout or a connection lost mid-request does not.
    private static bool NeverSent(Exception ex) => ex switch
    {
        BrokenCircuitException => true,
        HttpRequestException
        {
            HttpRequestError: HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError
            or HttpRequestError.SecureConnectionError or HttpRequestError.ProxyTunnelError
        } => true,
        HttpRequestException { HttpRequestError: HttpRequestError.Unknown, InnerException: null } => true,
        _ => false,
    };
}
