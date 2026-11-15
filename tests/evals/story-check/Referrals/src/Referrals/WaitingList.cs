namespace Referrals;

/// <summary>The wait a clinic shows to a patient who is about to be referred there.</summary>
public sealed class WaitingList(IReferralStore store)
{
    private static readonly TimeSpan PerReferral = TimeSpan.FromDays(2);

    public async Task<TimeSpan> EstimateAsync(int clinicId, CancellationToken ct)
    {
        var waiting = await store.CountAsync(clinicId, r => r.Status != ReferralStatus.Closed, ct);
        return PerReferral * waiting;
    }
}
