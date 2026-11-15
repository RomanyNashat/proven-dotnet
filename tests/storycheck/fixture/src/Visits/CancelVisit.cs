namespace Visits;

public sealed class CancelVisit(IVisitStore store, TimeProvider clock)
{
    public async Task<CancelResult> HandleAsync(int patientId, int visitId, CancellationToken ct)
    {
        var visit = await store.FindAsync(visitId, ct);
        if (visit is null || visit.PatientId != patientId)
            return CancelResult.NotFound;
        if (visit.StartsAt - clock.GetUtcNow() < TimeSpan.FromHours(2))
            return CancelResult.TooLate;
        return await store.CancelAsync(visit, ct);
    }
}
