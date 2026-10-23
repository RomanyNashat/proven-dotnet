namespace MeterSync.Application;

/// <summary>A reading as the vendor's API returns it (see VENDOR.md).</summary>
public sealed record VendorReading(string Id, string Value, string Unit, DateTimeOffset TakenAt);
