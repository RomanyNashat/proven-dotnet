using MeterSync.Domain;

namespace MeterSync.Application;

public interface IAlertStore
{
    /// <summary>Ids of pending alerts no pod holds at <paramref name="now"/>, oldest first.</summary>
    Task<IReadOnlyList<int>> DueAsync(DateTimeOffset now, int max, CancellationToken ct);

    /// <summary>Takes the alert until <paramref name="until"/>, unless another pod holds it or it's no longer pending. Atomic.</summary>
    Task<bool> TryClaimAsync(int readingId, DateTimeOffset now, DateTimeOffset until, CancellationToken ct);

    /// <summary>The reading, tracked for <see cref="SaveAsync"/>, with its patient's phone.</summary>
    Task<DueAlert?> LoadAsync(int readingId, CancellationToken ct);

    Task SaveAsync(CancellationToken ct);
}
