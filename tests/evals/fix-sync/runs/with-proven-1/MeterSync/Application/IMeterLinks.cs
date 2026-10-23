using MeterSync.Domain;

namespace MeterSync.Application;

public interface IMeterLinks
{
    /// <summary>The patient's national ID, or null when there is no such patient.</summary>
    Task<string?> NationalIdOfAsync(int patientId, CancellationToken ct);

    Task<bool> SerialTakenAsync(string serialNumber, CancellationToken ct);

    /// <summary>Saves the meter; false when another request linked the same serial first.</summary>
    Task<bool> TryAddAsync(Meter meter, CancellationToken ct);
}
