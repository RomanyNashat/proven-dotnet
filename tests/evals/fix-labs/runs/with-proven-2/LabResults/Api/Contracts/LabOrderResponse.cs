namespace LabResults.Api;

/// <summary>A lab order as the API returns it; the same JSON shape as the lab's own.</summary>
public sealed record LabOrderResponse(string OrderId, string Test, DateTimeOffset OrderedAt);
