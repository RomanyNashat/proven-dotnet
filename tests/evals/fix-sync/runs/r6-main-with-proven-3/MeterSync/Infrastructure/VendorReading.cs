namespace MeterSync.Infrastructure;

/// <summary>A reading as the vendor sends it (see VENDOR.md).</summary>
public sealed record VendorReading(string Id, string Value, string Unit, DateTimeOffset TakenAt);
