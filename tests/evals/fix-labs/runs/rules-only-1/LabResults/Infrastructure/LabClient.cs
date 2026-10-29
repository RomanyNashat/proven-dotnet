using System.Net.Http.Json;

namespace LabResults.Infrastructure;

public sealed record LabOrder(string OrderId, string Test, DateTimeOffset OrderedAt);

/// <summary>The lab network's API (see LABS.md).</summary>
public sealed class LabClient(HttpClient http)
{
    /// <summary>The patient's open lab orders.</summary>
    /// <param name="nationalId">A validated 10-digit national id; it becomes a path segment.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The orders.</returns>
    public async Task<IReadOnlyList<LabOrder>> OrdersAsync(string nationalId, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<LabOrder>>($"patients/{Uri.EscapeDataString(nationalId)}/orders", ct) ?? [];
}
