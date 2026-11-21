using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LabResults.Infrastructure;

public sealed record LabOrder(string OrderId, string Test, DateTimeOffset OrderedAt);

/// <summary>The lab network could not be reached or gave an unusable answer.</summary>
public sealed class LabUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The lab network's API (see LABS.md).</summary>
public sealed class LabClient(HttpClient http)
{
    /// <param name="nationalId">Must already be validated (digits only); it becomes a path segment.</param>
    public async Task<IReadOnlyList<LabOrder>> OrdersAsync(string nationalId, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"patients/{Uri.EscapeDataString(nationalId)}/orders", ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                // Deliberately no URI in the message: it contains the national ID.
                throw new LabUnavailableException($"Lab network answered {(int)response.StatusCode}.");
            }

            var orders = await response.Content.ReadFromJsonAsync<List<LabOrder>>(ct);
            return orders ?? [];
        }
        catch (HttpRequestException ex)
        {
            throw new LabUnavailableException("Lab network unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new LabUnavailableException("Lab network sent an unreadable answer.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LabUnavailableException("Lab network timed out.", ex);
        }
    }
}
