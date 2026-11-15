namespace SkillSamples.BusinessRules;

public enum CancelOutcome
{
    Cancelled,
    AlreadyCancelled,
    NotFound,
    TooLate,
    NeedsClinicCall,
}

public sealed record CancelResult(CancelOutcome Outcome, decimal Refund);

public sealed record Visit(int Id, int PatientId, DateTimeOffset StartsAt, decimal Paid, bool Cancelled, bool CancelledByClinic);

/// <summary>Cancels a booked visit. Written before anyone wrote its rules down: the rules are what it does.</summary>
public sealed class VisitCancellation(TimeProvider clock)
{
    public static readonly TimeSpan Cutoff = TimeSpan.FromHours(2);
    public static readonly TimeSpan FullRefundBefore = TimeSpan.FromHours(24);
    public const int CancellationsBeforeCall = 3;

    public CancelResult Cancel(int patientId, Visit? visit, int cancelledInLast30Days, bool byClinic)
    {
        if (visit is null || (!byClinic && visit.PatientId != patientId))
            return new(CancelOutcome.NotFound, 0m);
        if (visit.Cancelled)
            return new(CancelOutcome.AlreadyCancelled, 0m);
        if (byClinic)
            return new(CancelOutcome.Cancelled, visit.Paid);

        var left = visit.StartsAt - clock.GetUtcNow();
        if (left < Cutoff)
            return new(CancelOutcome.TooLate, 0m);
        if (cancelledInLast30Days + 1 >= CancellationsBeforeCall)
            return new(CancelOutcome.NeedsClinicCall, 0m);

        var refund = left >= FullRefundBefore ? visit.Paid : Math.Round(visit.Paid / 2, 2, MidpointRounding.ToZero);
        return new(CancelOutcome.Cancelled, refund);
    }
}
