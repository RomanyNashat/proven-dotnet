using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

public sealed record VendorReading(string Id, string Value, string Unit, DateTimeOffset TakenAt);

/// <summary>The meter vendor's cloud API (see VENDOR.md).</summary>
public sealed class MeterVendorClient(HttpClient http)
{
    public async Task<IReadOnlyList<VendorReading>> ReadingsAsync(string serialNumber, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<VendorReading>>(
            new Uri(new Uri(http.BaseAddress!.ToString().TrimEnd('/') + "/"), $"meters/{Uri.EscapeDataString(serialNumber)}/readings"), ct) ?? [];
}
