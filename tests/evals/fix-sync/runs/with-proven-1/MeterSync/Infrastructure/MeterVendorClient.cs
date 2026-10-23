using System.Net.Http.Json;
using MeterSync.Application;

namespace MeterSync.Infrastructure;

/// <summary>The meter vendor's cloud API (see VENDOR.md).</summary>
public sealed class MeterVendorClient(HttpClient http) : IMeterVendor
{
    // A relative path with no leading '/', so the base address keeps its "/v2/" (HttpClientSetup adds the
    // trailing slash). The serial is checked when the meter is linked: no '.', '/' or '%' reaches here.
    public async Task<IReadOnlyList<VendorReading>> ReadingsAsync(string serialNumber, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<VendorReading>>($"meters/{Uri.EscapeDataString(serialNumber)}/readings", ct) ?? [];
}
