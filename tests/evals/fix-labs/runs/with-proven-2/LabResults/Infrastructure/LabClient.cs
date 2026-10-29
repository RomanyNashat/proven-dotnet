using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LabResults.Application;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace LabResults.Infrastructure;

public sealed record LabOrder(string OrderId, string Test, DateTimeOffset OrderedAt);

/// <summary>The lab's answer: its orders, or that it couldn't be asked.</summary>
public sealed record LabLookup(bool Available, IReadOnlyList<LabOrder> Orders)
{
    public static LabLookup Unavailable { get; } = new(false, []);

    public static LabLookup Found(IReadOnlyList<LabOrder> orders) => new(true, orders);
}

/// <summary>
/// The lab network's API (see LABS.md). The national ID is in the URL path, so this client's
/// IHttpClientFactory logging is removed at registration and the lines here carry the status code only.
/// </summary>
public sealed class LabClient(HttpClient http, ILogger<LabClient> logger)
{
    /// <summary>The patient's open lab orders.</summary>
    /// <param name="nationalId">A national ID that passed <see cref="NationalIdFormat.IsValid"/>.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The orders (empty when the lab doesn't know the patient), or <see cref="LabLookup.Unavailable"/>.</returns>
    /// <exception cref="ArgumentException">The national ID isn't well-formed; nothing is sent.</exception>
    public async Task<LabLookup> OrdersAsync(string nationalId, CancellationToken ct)
    {
        // Checked here as well as at the endpoint: "..", or anything else not ten digits, would let the
        // caller reach other lab endpoints with the service's credentials.
        if (!NationalIdFormat.IsValid(nationalId))
        {
            throw new ArgumentException("Not a national ID.", nameof(nationalId));
        }

        try
        {
            using var response = await http.GetAsync($"patients/{nationalId}/orders", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return LabLookup.Found([]);
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Lab order lookup failed with status {StatusCode}", (int)response.StatusCode);
                return LabLookup.Unavailable;
            }

            return LabLookup.Found(await response.Content.ReadFromJsonAsync<List<LabOrder>>(ct) ?? []);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException
                                       or TimeoutRejectedException or BrokenCircuitException or JsonException)
        {
            logger.LogWarning("Lab order lookup failed with {ExceptionType}", ex.GetType().Name);
            return LabLookup.Unavailable;
        }
    }
}
