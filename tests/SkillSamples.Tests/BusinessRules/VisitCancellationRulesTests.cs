using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.BusinessRules;

// Characterization tests: each pins one rule from rules.md as the code does it today, so a refactor or an
// upgrade that changes a rule fails here first. They describe, they don't judge: R6's rounding is pinned
// as it is, and its question sits in rules.md.
public sealed class VisitCancellationRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private const int Patient = 7;

    private static Visit VisitIn(TimeSpan fromNow, decimal paid = 200m, bool cancelled = false) =>
        new(Id: 1, PatientId: Patient, StartsAt: Now + fromNow, Paid: paid, Cancelled: cancelled, CancelledByClinic: false);

    private static CancelResult Cancel(Visit? visit, int priorCancellations = 0, bool byClinic = false, int patient = Patient) =>
        new VisitCancellation(new FakeTimeProvider(Now)).Cancel(patient, visit, priorCancellations, byClinic);

    [Fact]
    [Trait("Rule", "R1")]
    public void Cancel_SomeoneElsesVisitOrNone_IsNotFound()
    {
        Cancel(VisitIn(TimeSpan.FromDays(3)), patient: 8).Should().Be(new CancelResult(CancelOutcome.NotFound, 0m));
        Cancel(null).Should().Be(new CancelResult(CancelOutcome.NotFound, 0m));
    }

    [Fact]
    [Trait("Rule", "R2")]
    public void Cancel_AlreadyCancelled_RefundsNothingMore() =>
        Cancel(VisitIn(TimeSpan.FromDays(3), cancelled: true)).Should().Be(new CancelResult(CancelOutcome.AlreadyCancelled, 0m));

    [Fact]
    [Trait("Rule", "R3"), Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Cancel_TheClinicCancelsInsideTheCutoff_ThePatientGetsEverythingBack()
    {
        // Given a visit in 30 minutes that the patient has cancelled twice before this month
        var soon = VisitIn(TimeSpan.FromMinutes(30), paid: 150m);

        // When the clinic cancels it (its doctor is off sick)
        var result = Cancel(soon, priorCancellations: 2, byClinic: true, patient: 0);

        // Then it is cancelled with a full refund: the cutoff and the repeat rule are the patient's, not the clinic's
        result.Should().Be(new CancelResult(CancelOutcome.Cancelled, 150m));
    }

    [Theory]
    [InlineData(-1, CancelOutcome.TooLate)]
    [InlineData(0, CancelOutcome.Cancelled)]
    [Trait("Rule", "R4")]
    public void Cancel_AtTheTwoHourCutoff_TheEdgeMayStillCancel(long ticks, CancelOutcome expected) =>
        Cancel(VisitIn(VisitCancellation.Cutoff + TimeSpan.FromTicks(ticks))).Outcome.Should().Be(expected);

    [Theory]
    [InlineData(1, CancelOutcome.Cancelled)]
    [InlineData(2, CancelOutcome.NeedsClinicCall)]
    [Trait("Rule", "R5")]
    public void Cancel_TheThirdInThirtyDays_NeedsAClinicCall(int prior, CancelOutcome expected) =>
        Cancel(VisitIn(TimeSpan.FromDays(3)), priorCancellations: prior).Outcome.Should().Be(expected);

    [Fact]
    [Trait("Rule", "R5"), Trait("Rule", "R4")]
    public void Cancel_TheThirdInsideTheCutoff_IsTooLateBeforeItNeedsACall() =>
        Cancel(VisitIn(TimeSpan.FromMinutes(90)), priorCancellations: 2).Outcome.Should().Be(CancelOutcome.TooLate);

    [Theory]
    [InlineData(24 * 60, "200", "200")]
    [InlineData(24 * 60 - 1, "200", "100")]
    [InlineData(3 * 60, "25.25", "12.62")]
    [Trait("Rule", "R6"), Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    public void Cancel_InTime_RefundsAllFromADayOutAndHalfRoundedDownCloser(int minutesAway, string paid, string refund)
    {
        // Given a patient who paid for a visit, when they cancel in time, then the refund depends on how far away it is
        var result = Cancel(VisitIn(TimeSpan.FromMinutes(minutesAway), paid: decimal.Parse(paid, System.Globalization.CultureInfo.InvariantCulture)));

        result.Should().Be(new CancelResult(CancelOutcome.Cancelled, decimal.Parse(refund, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    [Trait("Rule", "R6"), Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    public void Cancel_OnASlimImage_AVisitStoredWithTheClinicsOffsetCountsTheSameDay()
    {
        // A slim image: UTC, no time-zone database, no ICU. The visit was saved with the clinic's +03:00 offset.
        ProductionConditions.Require();
        var startsAt = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(3));   // 09:00 UTC tomorrow
        var visit = new Visit(1, Patient, startsAt, 200m, false, false);

        // Exactly 24 hours away in UTC, so all of it comes back, whatever the machine's own zone
        Cancel(visit).Should().Be(new CancelResult(CancelOutcome.Cancelled, 200m));
    }
}
