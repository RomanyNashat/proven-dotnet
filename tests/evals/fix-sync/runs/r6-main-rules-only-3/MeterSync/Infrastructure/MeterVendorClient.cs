using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

public sealed record VendorReading(string Id, string Value, string Unit, DateTimeOffset TakenAt);

/// <summary>The meter vendor's cloud API (see VENDOR.md).</summary>
public sealed class MeterVendorClient(HttpClient http)
{
    /// <summary>Every reading the meter has uploaded.</summary>
    /// <param name="serialNumber">The meter's serial number.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The readings; entries missing a field are dropped.</returns>
    public async Task<IReadOnlyList<VendorReading>> ReadingsAsync(string serialNumber, CancellationToken ct)
    {
        // Relative path: the base address ends in "/" (see Program), so this stays under /v2/.
        var readings = await http.GetFromJsonAsync<List<VendorReading?>>(
            $"meters/{Uri.EscapeDataString(serialNumber)}/readings", ct);
        return readings?
            .Where(r => r is { Id: not null, Value: not null, Unit: not null })
            .Select(r => r!)
            .ToList() ?? [];
    }
}
