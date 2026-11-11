using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

public sealed record VendorReading(string Id, string Value, string Unit, DateTimeOffset TakenAt);

/// <summary>The meter vendor's cloud API (see VENDOR.md).</summary>
public sealed class MeterVendorClient(HttpClient http)
{
    /// <summary>Every reading the meter has uploaded.</summary>
    /// <param name="serialNumber">The meter's serial number.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The readings, oldest or newest first as the vendor returns them.</returns>
    public async Task<IReadOnlyList<VendorReading>> ReadingsAsync(string serialNumber, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<VendorReading>>($"meters/{Uri.EscapeDataString(serialNumber)}/readings", ct) ?? [];
}
