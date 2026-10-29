using System.Net;
using System.Net.Http.Json;

namespace LabResults.Infrastructure;

public sealed record LabOrder(string OrderId, string Test, DateTimeOffset OrderedAt);

/// <summary>The lab network's API (see LABS.md).</summary>
public sealed class LabClient(HttpClient http)
{
    /// <summary>The patient's open orders; none when the lab doesn't know the patient.</summary>
    /// <param name="nationalId">A validated national ID, in Western digits.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The orders.</returns>
    /// <exception cref="HttpRequestException">The lab answered with an error other than 404.</exception>
    public async Task<IReadOnlyList<LabOrder>> OrdersAsync(string nationalId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"patients/{Uri.EscapeDataString(nationalId)}/orders", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<LabOrder>>(ct) ?? [];
    }
}
