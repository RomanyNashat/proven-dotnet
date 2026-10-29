using System.Net;
using System.Net.Http.Json;

namespace LabResults.Infrastructure;

public sealed record LabOrder(string OrderId, string Test, DateTimeOffset OrderedAt);

/// <summary>
/// The lab network's API (see LABS.md). The national ID is in the URL path, so the client's request
/// logging is removed at registration and this class logs only the status code.
/// </summary>
public sealed class LabClient(HttpClient http, ILogger<LabClient> logger)
{
    /// <summary>The patient's open orders; empty when the lab doesn't know the patient.</summary>
    /// <param name="nationalId">A validated 10-digit ASCII national ID.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The orders.</returns>
    /// <exception cref="HttpRequestException">The lab answered with an error other than 404.</exception>
    public async Task<IReadOnlyList<LabOrder>> OrdersAsync(string nationalId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"patients/{Uri.EscapeDataString(nationalId)}/orders", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Lab order lookup failed with status {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("The lab API refused the order lookup.", null, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<List<LabOrder>>(ct) ?? [];
    }
}
