using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

/// <summary>One reading as the vendor sends it; any field may be missing.</summary>
public sealed record VendorReading(string? Id, string? Value, string? Unit, DateTimeOffset TakenAt);

/// <summary>The meter vendor's cloud API (see VENDOR.md).</summary>
public sealed class MeterVendorClient(HttpClient http)
{
    public async Task<IReadOnlyList<VendorReading>> ReadingsAsync(string serialNumber, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<VendorReading>>($"meters/{Uri.EscapeDataString(serialNumber)}/readings", ct) ?? [];
}
