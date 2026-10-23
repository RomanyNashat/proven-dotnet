using MeterSync.Domain;

namespace MeterSync.Application;

/// <summary>Links a meter to the calling patient.</summary>
public sealed class MeterLinking(IMeterLinks links, TimeProvider time)
{
    public async Task<LinkResult> LinkAsync(int callerId, string nationalId, string serialNumber, CancellationToken ct)
    {
        // A patient links meters to themselves only. Someone else's national ID gets the same answer as an
        // unknown one, so the endpoint can't be used to find out who is registered.
        var callersNationalId = await links.NationalIdOfAsync(callerId, ct);
        if (callersNationalId is null || !string.Equals(callersNationalId, nationalId, StringComparison.Ordinal))
        {
            return new LinkResult(LinkOutcome.NotFound);
        }

        if (await links.SerialTakenAsync(serialNumber, ct))
        {
            return new LinkResult(LinkOutcome.AlreadyLinked);
        }

        var meter = new Meter(callerId, serialNumber, time.GetUtcNow());
        return await links.TryAddAsync(meter, ct)
            ? new LinkResult(LinkOutcome.Linked, meter)
            : new LinkResult(LinkOutcome.AlreadyLinked);
    }
}
