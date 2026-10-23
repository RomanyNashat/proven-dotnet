namespace MeterSync.Application;

public interface IMeterVendor
{
    Task<IReadOnlyList<VendorReading>> ReadingsAsync(string serialNumber, CancellationToken ct);
}
