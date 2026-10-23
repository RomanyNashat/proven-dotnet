using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>Pulls new readings from the vendor for every linked meter.</summary>
public sealed class MeterSyncService(ISyncStore store, IMeterVendor vendor, ILogger<MeterSyncService> logger)
{
    public async Task<int> SyncOnceAsync(CancellationToken ct = default)
    {
        var meters = await store.MetersAsync(ct);
        var added = 0;
        foreach (var meter in meters)
        {
            // One meter's failure (a 404 for a mistyped serial, a vendor hiccup) must not cost the others
            // their readings, so each meter is fetched and saved on its own.
            try
            {
                added += await SyncMeterAsync(meter, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // No exception object: its message can carry the vendor URL with the serial number.
                logger.LogWarning("Sync of meter {MeterId} failed: {ExceptionType} {StatusCode}",
                    meter.Id, ex.GetType().Name, (ex as HttpRequestException)?.StatusCode);
            }
        }

        return added;
    }

    private async Task<int> SyncMeterAsync(Meter meter, CancellationToken ct)
    {
        var fromVendor = await vendor.ReadingsAsync(meter.SerialNumber, ct);
        var readings = new List<Reading>(fromVendor.Count);
        foreach (var r in fromVendor)
        {
            if (Reading.TryFromVendor(meter, r.Id, r.Value, r.Unit, r.TakenAt, out var reading, out var rejection))
            {
                readings.Add(reading);
            }
            else
            {
                // The vendor id only: the value is health data.
                logger.LogWarning("Skipping reading {VendorId} of meter {MeterId}: {Reason}", r.Id, meter.Id, rejection);
            }
        }

        return readings.Count == 0 ? 0 : await store.AddNewAsync(readings, ct);
    }
}
