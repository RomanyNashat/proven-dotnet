using System.Net.Http.Json;

namespace LabResults.Infrastructure;

public sealed record LabOrder(string OrderId, string Test, DateTimeOffset OrderedAt);

/// <summary>The lab network's API (see LABS.md).</summary>
public sealed class LabClient(HttpClient http)
{
    public async Task<IReadOnlyList<LabOrder>> OrdersAsync(string nationalId, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<LabOrder>>($"patients/{Uri.EscapeDataString(nationalId)}/orders", ct) ?? [];
}
