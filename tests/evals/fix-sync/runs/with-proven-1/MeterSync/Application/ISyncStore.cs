using MeterSync.Domain;

namespace MeterSync.Application;

public interface ISyncStore
{
    Task<IReadOnlyList<Meter>> MetersAsync(CancellationToken ct);

    /// <summary>Stores the readings not stored yet (by vendor id), safely with other pods doing the same. Returns how many were new.</summary>
    Task<int> AddNewAsync(IReadOnlyCollection<Reading> readings, CancellationToken ct);
}
