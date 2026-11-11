using System.Net.Http.Json;

namespace MeterSync.Infrastructure;

public sealed record VendorReading(string Id, string Value, string Unit, DateTimeOffset TakenAt);

/// <summary>The meter vendor's cloud API (see VENDOR.md).</summary>
public sealed class MeterVendorClient(HttpClient http)
{
    /// <summary>Every reading the meter has uploaded.</summary>
    /// <param name="serialNumber">The meter's serial number.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The readings, oldest or newest first as the vendor sends them.</returns>
    public async Task<IReadOnlyList<VendorReading>> ReadingsAsync(string serialNumber, CancellationToken ct) =>
        await http.GetFromJsonAsync<List<VendorReading>>($"meters/{Uri.EscapeDataString(serialNumber)}/readings", ct) ?? [];

    /// <summary>
    /// A base address that keeps its path when a relative URL is resolved against it: without the trailing
    /// slash, <c>https://meters.example.net/v2</c> + <c>meters/…</c> becomes <c>https://meters.example.net/meters/…</c>.
    /// </summary>
    /// <param name="configured">The base address from configuration.</param>
    /// <returns>The same address, ending in a slash.</returns>
    public static Uri BaseAddressFrom(string configured) =>
        new(configured.EndsWith('/') ? configured : configured + "/", UriKind.Absolute);
}
