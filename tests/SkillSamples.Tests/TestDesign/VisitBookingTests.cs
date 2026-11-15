using FluentAssertions;
using SkillSamples.Production;
using Xunit;

namespace SkillSamples.TestDesign;

// The cases for story.md, designed before the code: a decision table for who may book, boundary values for
// when, a state-transition table for the status. Each test carries the criteria it proves.
public sealed class VisitBookingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tomorrow = Now.AddDays(1);
    private static readonly Person Guardian = new(1, new DateOnly(1985, 3, 10));
    private static readonly Person Adult = new(2, new DateOnly(1990, 6, 1));
    private static readonly Person Child = new(3, new DateOnly(2015, 9, 20));

    // Decision table: who books (self / other), linked guardian (yes / no), patient adult on the visit day.
    [Theory]
    [InlineData("self", false, true, BookingDecision.Allowed)]
    [InlineData("self", false, false, BookingDecision.NeedsGuardian)]
    [InlineData("other", true, false, BookingDecision.Allowed)]
    [InlineData("other", true, true, BookingDecision.PatientBooksThemself)]
    [InlineData("other", false, false, BookingDecision.NotYourDependent)]
    [InlineData("other", false, true, BookingDecision.NotYourDependent)]
    [Trait("AC", "AC1"), Trait("AC", "AC2"), Trait("AC", "AC3")]
    public void Decide_WhoBooksForWhom_FollowsTheDecisionTable(string booker, bool linked, bool adult, BookingDecision expected)
    {
        var patient = adult ? Adult : Child;
        var by = booker == "self" ? patient : Guardian;

        VisitBooking.Decide(by, patient, linked, Tomorrow, Now).Should().Be(expected);
    }

    // Boundary values on both edges of the window, one tick either side.
    [Theory]
    [InlineData(-1, BookingDecision.TooSoon)]
    [InlineData(0, BookingDecision.Allowed)]
    [Trait("AC", "AC4")]
    public void Decide_AtTheTwoHourNotice_TheEdgeIsAllowed(long ticks, BookingDecision expected) =>
        VisitBooking.Decide(Adult, Adult, false, Now + VisitBooking.MinimumNotice + TimeSpan.FromTicks(ticks), Now)
            .Should().Be(expected);

    [Theory]
    [InlineData(0, BookingDecision.Allowed)]
    [InlineData(1, BookingDecision.TooFarAhead)]
    [Trait("AC", "AC4")]
    public void Decide_AtThirtyDaysAhead_TheEdgeIsAllowed(long ticks, BookingDecision expected) =>
        VisitBooking.Decide(Adult, Adult, false, Now + VisitBooking.MaximumAhead + TimeSpan.FromTicks(ticks), Now)
            .Should().Be(expected);

    // State transitions: every pair, so a move nobody asked for can't slip in.
    public static TheoryData<VisitStatus, VisitStatus, bool> EveryMove()
    {
        var allowed = new[] { (VisitStatus.Booked, VisitStatus.CheckedIn), (VisitStatus.CheckedIn, VisitStatus.Completed), (VisitStatus.Booked, VisitStatus.Cancelled) };
        var data = new TheoryData<VisitStatus, VisitStatus, bool>();
        foreach (var from in Enum.GetValues<VisitStatus>())
            foreach (var to in Enum.GetValues<VisitStatus>())
                data.Add(from, to, allowed.Contains((from, to)));
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryMove))]
    [Trait("AC", "AC5")]
    public void CanMove_EveryPairOfStatuses_OnlyTheThreeMovesAreAllowed(VisitStatus from, VisitStatus to, bool expected) =>
        VisitBooking.CanMove(from, to).Should().Be(expected);

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Story)]
    [Trait("AC", "AC2")]
    public void Story_ADependentTurnsEighteenAtMidnightInRiyadh_TheGuardianCantBookTheVisit()
    {
        // Given a dependent born on 10 December 2008, and a visit at 00:30 on 10 December 2026 in Riyadh,
        // which is 21:30 on the 9th in UTC
        var dependent = new Person(4, new DateOnly(2008, 12, 10));
        var visit = new DateTimeOffset(2026, 12, 10, 0, 30, 0, VisitBooking.ClinicOffset);
        visit.UtcDateTime.Day.Should().Be(9);

        // When the guardian books it
        var decision = VisitBooking.Decide(Guardian, dependent, isLinkedGuardian: true, visit, Now);

        // Then the dependent is 18 on the day of the visit at the clinic, so they book for themself.
        // Counting the day in UTC would have made them 17 and let the guardian book.
        decision.Should().Be(BookingDecision.PatientBooksThemself);
    }

    [Fact]
    [Trait(ProductionConditions.Trait, ProductionConditions.Production)]
    [Trait("AC", "AC2")]
    public void Production_NoTzdata_TheClinicDateNeedsNoTimeZoneDatabase()
    {
        // On a slim image without tzdata, looking up Asia/Riyadh would throw; the fixed offset doesn't.
        ProductionConditions.Require();

        VisitBooking.ClinicDate(new DateTimeOffset(2026, 12, 15, 21, 30, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2026, 12, 16));
    }
}
