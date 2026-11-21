using FluentAssertions;
using static RefillDesk.Tests.DeskFixture;

namespace RefillDesk.Tests;

public sealed class RequestTests
{
    private static readonly DateOnly Yesterday = Today.AddDays(-1);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);

    /// <summary>LastDispensedOn that puts runsOut-7 at <paramref name="windowOpensInDays"/> from today.
    /// The 7 is a literal on purpose: deriving it from Desk.WindowDays would let a changed constant pass.</summary>
    private static DateOnly LastDispensedFor(int daysSupply, int windowOpensInDays)
        => Today.AddDays(windowOpensInDays + 7 - daysSupply);

    // ---------- AC1: NotFound ----------

    [Fact]
    [Trait("AC", "AC1")]
    public void Request_UnknownPrescription_ReturnsNotFoundAndStoresNothing()
    {
        var f = new DeskFixture();
        f.Add(Rx(id: 1));

        var result = f.Desk.Request(PatientId, prescriptionId: 2);

        result.Should().Be(new RefillResult(RefillRefusal.NotFound, null));
        f.Store.Adds.Should().Be(0);
        f.Store.Saves.Should().Be(0);
        f.Inner.RequestsFor(2).Should().BeEmpty();
    }

    // ---------- AC2: who may ask ----------

    [Fact]
    [Trait("AC", "AC2"), Trait("EC", "EC10")]
    public void Request_PatientWithoutAnyDelegation_IsAccepted()
    {
        var f = new DeskFixture();
        f.Add(Rx());

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.None);
    }

    // Boundary: From and To are both included; a day outside either end is refused.
    [Theory]
    [InlineData(0, 0, RefillRefusal.None)]      // one-day delegation, today
    [InlineData(0, 5, RefillRefusal.None)]      // From == today
    [InlineData(1, 5, RefillRefusal.NotYourPrescription)]  // From == tomorrow: not started
    [InlineData(-5, 0, RefillRefusal.None)]     // To == today
    [InlineData(-5, -1, RefillRefusal.NotYourPrescription)] // To == yesterday: expired
    [InlineData(-1, 1, RefillRefusal.None)]
    [Trait("AC", "AC2"), Trait("EC", "EC1"), Trait("EC", "EC2"), Trait("EC", "EC3"), Trait("EC", "EC4")]
    public void Request_CaregiverDelegationDays_FromAndToAreInclusive(int fromDays, int toDays, RefillRefusal expected)
    {
        var f = new DeskFixture();
        f.Add(Rx());
        f.Delegate(CaregiverId, PatientId, Today.AddDays(fromDays), Today.AddDays(toDays));

        var result = f.Desk.Request(CaregiverId, 1);

        result.Refusal.Should().Be(expected);
        result.Ok.Should().Be(expected == RefillRefusal.None);
    }

    [Fact]
    [Trait("AC", "AC2"), Trait("EC", "EC5")]
    public void Request_CaregiverDelegatedByAnotherPatient_IsNotYourPrescription()
    {
        var f = new DeskFixture();
        f.Add(Rx(patientId: PatientId));
        f.Delegate(CaregiverId, OtherPatientId, Yesterday, Tomorrow);

        f.Desk.Request(CaregiverId, 1).Should().Be(new RefillResult(RefillRefusal.NotYourPrescription, null));
    }

    [Fact]
    [Trait("AC", "AC2"), Trait("EC", "EC6")]
    public void Request_AnotherCaregiversDelegation_IsNotYourPrescription()
    {
        var f = new DeskFixture();
        f.Add(Rx());
        f.Delegate(OtherCaregiverId, PatientId, Yesterday, Tomorrow);

        f.Desk.Request(CaregiverId, 1).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
        f.Desk.Request(OtherCaregiverId, 1).Refusal.Should().Be(RefillRefusal.None);
    }

    [Theory]
    [InlineData(StrangerId)]
    [InlineData(OtherPatientId)]
    [InlineData(0)]
    [Trait("AC", "AC2"), Trait("EC", "EC7")]
    public void Request_StrangerWithNoDelegation_IsNotYourPrescription(int requesterId)
    {
        var f = new DeskFixture();
        f.Add(Rx());
        f.Add(Rx(id: 2, patientId: OtherPatientId));

        var result = f.Desk.Request(requesterId, 1);

        result.Should().Be(new RefillResult(RefillRefusal.NotYourPrescription, null));
        f.Store.Adds.Should().Be(0);
    }

    // Decision table over a caregiver's delegations: (right patient?, covers today?) per delegation.
    [Theory]
    [InlineData(true, RefillRefusal.None)]
    [InlineData(false, RefillRefusal.NotYourPrescription)]
    [Trait("AC", "AC2"), Trait("EC", "EC8")]
    public void Request_CaregiverWithSeveralDelegations_AcceptedOnlyWhenOneIsForThisPatientToday(bool includeTheValidOne, RefillRefusal expected)
    {
        var f = new DeskFixture();
        f.Add(Rx());
        f.Delegate(CaregiverId, PatientId, Today.AddDays(-30), Yesterday);     // right patient, expired
        f.Delegate(CaregiverId, OtherPatientId, Yesterday, Tomorrow);          // current, wrong patient
        f.Delegate(CaregiverId, PatientId, Tomorrow, Today.AddDays(30));       // right patient, not started
        if (includeTheValidOne)
            f.Delegate(CaregiverId, PatientId, Today, Today);

        f.Desk.Request(CaregiverId, 1).Refusal.Should().Be(expected);
    }

    [Fact]
    [Trait("AC", "AC2"), Trait("EC", "EC9")]
    public void Request_DelegationWithFromAfterTo_IsNotYourPrescription()
    {
        var f = new DeskFixture();
        f.Add(Rx());
        f.Delegate(CaregiverId, PatientId, Tomorrow, Yesterday);

        f.Desk.Request(CaregiverId, 1).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    // ---------- AC3: expiry ----------

    [Theory]
    [InlineData(1, RefillRefusal.None)]
    [InlineData(0, RefillRefusal.None)]       // ExpiresOn == today
    [InlineData(-1, RefillRefusal.Expired)]   // today is the day after ExpiresOn
    [InlineData(-400, RefillRefusal.Expired)]
    [Trait("AC", "AC3"), Trait("EC", "EC11"), Trait("EC", "EC12")]
    public void Request_ExpiresOnRelativeToToday_ExpiredOnlyFromTheDayAfter(int expiresInDays, RefillRefusal expected)
    {
        var f = new DeskFixture();
        f.Add(Rx(expiresOn: Today.AddDays(expiresInDays)));

        var result = f.Desk.Request(PatientId, 1);

        result.Refusal.Should().Be(expected);
        (result.Request is null).Should().Be(expected != RefillRefusal.None);
    }

    // ---------- AC4: refills left ----------

    [Theory]
    [InlineData(100, RefillRefusal.None)]
    [InlineData(1, RefillRefusal.None)]
    [InlineData(0, RefillRefusal.NoRefillsLeft)]
    [Trait("AC", "AC4"), Trait("EC", "EC13")]
    public void Request_RefillsLeft_OneIsAcceptedZeroIsRefused(int refillsLeft, RefillRefusal expected)
    {
        var f = new DeskFixture();
        f.Add(Rx(refillsLeft: refillsLeft));

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(expected);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [Trait("AC", "AC4"), Trait("EC", "EC14")]
    public void Request_NegativeRefillsLeft_IsNoRefillsLeft(int refillsLeft)
    {
        var f = new DeskFixture();
        f.Add(Rx(refillsLeft: refillsLeft));

        f.Desk.Request(PatientId, 1).Should().Be(new RefillResult(RefillRefusal.NoRefillsLeft, null));
    }

    // ---------- AC5: the refill window ----------

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(7)]
    [InlineData(1)]
    [InlineData(0)]
    [Trait("AC", "AC5"), Trait("EC", "EC15")]
    public void Request_TodayIsExactlyRunsOutMinusSeven_IsAccepted(int daysSupply)
    {
        var f = new DeskFixture();
        f.Add(Rx(daysSupply: daysSupply, lastDispensedOn: LastDispensedFor(daysSupply, windowOpensInDays: 0)));

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.None);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(8)]
    [Trait("AC", "AC5"), Trait("EC", "EC16")]
    public void Request_TodayIsOneDayBeforeTheWindow_IsTooEarly(int daysSupply)
    {
        var f = new DeskFixture();
        f.Add(Rx(daysSupply: daysSupply, lastDispensedOn: LastDispensedFor(daysSupply, windowOpensInDays: 1)));

        f.Desk.Request(PatientId, 1).Should().Be(new RefillResult(RefillRefusal.TooEarly, null));
    }

    [Fact]
    [Trait("AC", "AC5"), Trait("EC", "EC16")]
    public void Request_DispensedTodayWithEightDaysSupply_IsTooEarly()
    {
        var f = new DeskFixture();
        f.Add(Rx(daysSupply: 8, lastDispensedOn: Today));

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.TooEarly);
    }

    [Theory]
    [InlineData(-7)]   // today == runsOut
    [InlineData(-8)]   // the day after runsOut
    [InlineData(-365)] // a year late
    [Trait("AC", "AC5"), Trait("EC", "EC17")]
    public void Request_OnOrAfterRunsOut_IsAccepted(int windowOpensInDays)
    {
        var f = new DeskFixture();
        f.Add(Rx(daysSupply: 30, lastDispensedOn: LastDispensedFor(30, windowOpensInDays)));

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.None);
    }

    // ---------- AC6: AlreadyOpen ----------

    [Theory]
    [InlineData(RefillStatus.Requested, RefillRefusal.AlreadyOpen)]
    [InlineData(RefillStatus.PendingApproval, RefillRefusal.AlreadyOpen)]
    [Trait("AC", "AC6"), Trait("EC", "EC18")]
    public void Request_ExistingOpenRequest_IsAlreadyOpen(RefillStatus existing, RefillRefusal expected)
    {
        var f = new DeskFixture();
        f.Add(Rx());
        f.Seed(1, existing);

        var result = f.Desk.Request(PatientId, 1);

        result.Should().Be(new RefillResult(expected, null));
        f.Inner.RequestsFor(1).Should().ContainSingle();
    }

    [Theory]
    [InlineData(RefillStatus.Approved)]
    [InlineData(RefillStatus.Rejected)]
    [InlineData(RefillStatus.Dispensed)]
    [InlineData(RefillStatus.Cancelled)]
    [Trait("AC", "AC6"), Trait("EC", "EC19")]
    public void Request_ExistingClosedOrApprovedRequest_DoesNotBlock(RefillStatus existing)
    {
        var f = new DeskFixture();
        f.Add(Rx());
        f.Seed(1, existing);

        var result = f.Desk.Request(PatientId, 1);

        result.Refusal.Should().Be(RefillRefusal.None);
        f.Inner.RequestsFor(1).Select(r => r.Status).Should().Equal(existing, RefillStatus.Requested);
    }

    [Fact]
    [Trait("AC", "AC6"), Trait("EC", "EC19")]
    public void Request_WhileAnotherIsApprovedButNotDispensed_IsAccepted()
    {
        // QUESTION: pinned as it is. Two requests can be Approved at once and both dispensed.
        var f = new DeskFixture();
        f.Add(Rx());
        var first = f.Reach(RefillStatus.Approved);

        var second = f.Desk.Request(PatientId, 1);

        second.Ok.Should().BeTrue();
        second.Request!.Id.Should().NotBe(first.Id);
    }

    [Theory]
    [InlineData(RefillStatus.Requested)]
    [InlineData(RefillStatus.PendingApproval)]
    [Trait("AC", "AC6"), Trait("EC", "EC20")]
    public void Request_OpenRequestOnAnotherPrescription_DoesNotBlock(RefillStatus existing)
    {
        var f = new DeskFixture();
        f.Add(Rx(id: 1));
        f.Add(Rx(id: 2, medicine: "Atorvastatin"));
        f.Seed(2, existing);

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.None);
    }

    [Theory]
    [InlineData(false, PatientId)]
    [InlineData(false, CaregiverId)]
    [InlineData(true, PatientId)]
    [InlineData(true, CaregiverId)]
    [Trait("AC", "AC6"), Trait("EC", "EC21")]
    public void Request_SecondRequestAfterAnAcceptedOne_IsAlreadyOpen(bool controlled, int secondRequester)
    {
        var f = new DeskFixture();
        f.Add(Rx(controlled: controlled));
        f.Delegate(CaregiverId, PatientId, Yesterday, Tomorrow);
        f.Desk.Request(PatientId, 1).Ok.Should().BeTrue();

        var second = f.Desk.Request(secondRequester, 1);

        second.Should().Be(new RefillResult(RefillRefusal.AlreadyOpen, null));
        f.Store.Adds.Should().Be(1);
    }

    // ---------- AC7: the order of the refusals ----------

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC22")]
    public void Request_MissingPrescriptionWithStrangerAndOpenRequest_IsNotFound()
    {
        var f = new DeskFixture();
        f.Seed(prescriptionId: 5, RefillStatus.Requested);

        f.Desk.Request(StrangerId, 5).Refusal.Should().Be(RefillRefusal.NotFound);
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC23")]
    public void Request_NotYoursAndExpired_IsNotYourPrescription()
    {
        var f = new DeskFixture();
        f.Add(Rx(expiresOn: Yesterday));

        f.Desk.Request(StrangerId, 1).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.Expired);
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC24")]
    public void Request_ExpiredAndNoRefillsLeft_IsExpired()
    {
        var f = new DeskFixture();
        f.Add(Rx(expiresOn: Yesterday, refillsLeft: 0));

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.Expired);
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC25")]
    public void Request_NoRefillsLeftAndTooEarly_IsNoRefillsLeft()
    {
        var f = new DeskFixture();
        f.Add(Rx(refillsLeft: 0, lastDispensedOn: Today));

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.NoRefillsLeft);
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC26")]
    public void Request_TooEarlyAndAlreadyOpen_IsTooEarly()
    {
        var f = new DeskFixture();
        f.Add(Rx(lastDispensedOn: Today));
        f.Seed(1, RefillStatus.Requested);

        f.Desk.Request(PatientId, 1).Refusal.Should().Be(RefillRefusal.TooEarly);
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC27")]
    public void Request_EveryRefusalApplies_IsNotYourPrescription()
    {
        var f = new DeskFixture();
        f.Add(Rx(expiresOn: Yesterday, refillsLeft: 0, lastDispensedOn: Today));
        f.Seed(1, RefillStatus.PendingApproval);
        f.Delegate(CaregiverId, PatientId, Today.AddDays(-10), Yesterday);

        f.Desk.Request(CaregiverId, 1).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    // ---------- AC8: the accepted request ----------

    [Theory]
    [InlineData(false, RefillStatus.Requested)]
    [InlineData(true, RefillStatus.PendingApproval)]
    [Trait("AC", "AC8"), Trait("EC", "EC28")]
    public void Request_Accepted_StatusDependsOnControlled(bool controlled, RefillStatus expected)
    {
        var f = new DeskFixture();
        f.Add(Rx(id: 7, controlled: controlled));

        var result = f.Desk.Request(PatientId, 7);

        result.Ok.Should().BeTrue();
        result.Refusal.Should().Be(RefillRefusal.None);
        result.Request.Should().NotBeNull();
        result.Request!.Status.Should().Be(expected);
        result.Request.PrescriptionId.Should().Be(7);
        result.Request.RequestedBy.Should().Be(PatientId);
        result.Request.Id.Should().Be(1);
    }

    [Fact]
    [Trait("AC", "AC8"), Trait("EC", "EC29")]
    public void Request_ByCaregiver_RecordsTheCaregiverAsRequestedBy()
    {
        var f = new DeskFixture();
        f.Add(Rx(id: 3));
        f.Delegate(CaregiverId, PatientId, Today, Today);

        var result = f.Desk.Request(CaregiverId, 3);

        result.Request!.RequestedBy.Should().Be(CaregiverId);
        result.Request.PrescriptionId.Should().Be(3);
        result.Request.Status.Should().Be(RefillStatus.Requested);
    }

    [Fact]
    [Trait("AC", "AC8"), Trait("EC", "EC30")]
    public void Request_Accepted_IsStoredAndFindRequestReturnsTheSameInstance()
    {
        var f = new DeskFixture();
        f.Add(Rx());
        f.Seed(1, RefillStatus.Cancelled);

        var result = f.Desk.Request(PatientId, 1);

        result.Request!.Id.Should().Be(2);
        f.Store.Adds.Should().Be(1);
        f.Inner.FindRequest(2).Should().BeSameAs(result.Request);
        f.Inner.RequestsFor(1).Should().HaveCount(2).And.Contain(result.Request);
    }

    public static TheoryData<string, RefillRefusal> EveryRefusal() => new()
    {
        { "unknown prescription", RefillRefusal.NotFound },
        { "stranger", RefillRefusal.NotYourPrescription },
        { "expired", RefillRefusal.Expired },
        { "no refills", RefillRefusal.NoRefillsLeft },
        { "too early", RefillRefusal.TooEarly },
        { "already open", RefillRefusal.AlreadyOpen },
    };

    [Theory]
    [MemberData(nameof(EveryRefusal))]
    [Trait("AC", "AC8"), Trait("EC", "EC31")]
    public void Request_Refused_ReturnsNoRequestAndChangesNothing(string scenario, RefillRefusal expected)
    {
        var f = new DeskFixture();
        var rx = f.Add(scenario switch
        {
            "expired" => Rx(expiresOn: Yesterday),
            "no refills" => Rx(refillsLeft: 0),
            "too early" => Rx(lastDispensedOn: Today),
            _ => Rx(),
        });
        var existing = scenario == "already open" ? f.Seed(1, RefillStatus.Requested) : null;
        var addsBefore = f.Store.Adds;
        var requester = scenario == "stranger" ? StrangerId : PatientId;
        var prescriptionId = scenario == "unknown prescription" ? 99 : 1;

        var result = f.Desk.Request(requester, prescriptionId);

        result.Refusal.Should().Be(expected);
        result.Request.Should().BeNull();
        result.Ok.Should().BeFalse();
        f.Store.Adds.Should().Be(addsBefore);
        f.Store.Saves.Should().Be(0);
        f.Inner.FindPrescription(1).Should().Be(rx);
        f.Inner.RequestsFor(1).Should().Equal(existing is null ? [] : [existing]);
        if (existing is not null)
            existing.Status.Should().Be(RefillStatus.Requested);
    }

    [Fact]
    [Trait("AC", "AC8"), Trait("EC", "EC32")]
    public void Request_Accepted_DoesNotChangeOrSaveThePrescription()
    {
        var f = new DeskFixture();
        var rx = f.Add(Rx());

        f.Desk.Request(PatientId, 1).Ok.Should().BeTrue();

        f.Store.Saves.Should().Be(0);
        f.Inner.FindPrescription(1).Should().BeSameAs(rx);
    }
}
