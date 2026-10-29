using System.Net;
using System.Net.Http.Json;

namespace LabResults.Infrastructure;

/// <summary>The lab network's API (see LABS.md).</summary>
public sealed class LabClient(HttpClient http)
{
    /// <summary>The patient's open orders; none when the lab doesn't know the patient.</summary>
    /// <param name="nationalId">The patient's national ID, already validated as ten digits.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The open orders.</returns>
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
