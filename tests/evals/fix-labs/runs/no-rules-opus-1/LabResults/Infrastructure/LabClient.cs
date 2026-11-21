using System.Net;
using System.Net.Http.Json;

namespace LabResults.Infrastructure;

public sealed record LabOrder(string OrderId, string Test, DateTimeOffset OrderedAt);

/// <summary>The lab network could not be reached or answered with an error.</summary>
public sealed class LabUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The lab network's API (see LABS.md).</summary>
public sealed class LabClient(HttpClient http)
{
    /// <summary>The patient's open lab orders; empty when the lab does not know the patient.</summary>
    /// <remarks>
    /// The caller must have validated <paramref name="nationalId"/>; it goes into the URL path.
    /// Exceptions deliberately do not carry the URL, which contains the national ID.
    /// </remarks>
    public async Task<IReadOnlyList<LabOrder>> OrdersAsync(string nationalId, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync($"patients/{Uri.EscapeDataString(nationalId)}/orders", ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LabUnavailableException("The lab network could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LabUnavailableException("The lab network timed out.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new LabUnavailableException($"The lab network answered {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<List<LabOrder>>(ct) ?? [];
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or NotSupportedException)
            {
                throw new LabUnavailableException("The lab network sent an unreadable response.", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new LabUnavailableException("The lab network timed out.", ex);
            }
        }
    }
}
