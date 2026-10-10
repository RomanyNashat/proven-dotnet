using System.Net.Http.Json;

namespace SkillSamples.Core;

public sealed record Shipment(int OrderId, string TrackingNumber);

public sealed class ShippingClient(HttpClient http)
{
    // A relative path with no leading '/': "/shipments" would drop the base address's path ("/api/").
    public async Task<Shipment> CreateAsync(int orderId, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("shipments", new { orderId }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Shipment>(ct)
            ?? throw new InvalidOperationException("The shipping service answered with an empty body.");
    }
}
