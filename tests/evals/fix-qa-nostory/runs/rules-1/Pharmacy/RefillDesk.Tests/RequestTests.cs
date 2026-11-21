using FluentAssertions;
using static RefillDesk.Tests.TestWorld;

namespace RefillDesk.Tests;

public sealed class RequestTests
{
    // ---------- successful requests ----------

    [Fact]
    public void Patient_NonControlled_CreatesRequestedRequest()
    {
        var w = new TestWorld();
        w.AddRx();

        var result = w.Desk.Request(Patient, RxId);

        result.Ok.Should().BeTrue();
        result.Refusal.Should().Be(RefillRefusal.None);
        result.Request.Should().NotBeNull();
        result.Request!.Status.Should().Be(RefillStatus.Requested);
        result.Request.PrescriptionId.Should().Be(RxId);
        result.Request.RequestedBy.Should().Be(Patient);
        w.Store.RequestsFor(RxId).Should().ContainSingle().Which.Should().BeSameAs(result.Request);
        w.Store.FindRequest(result.Request.Id).Should().BeSameAs(result.Request);
    }

    [Fact]
    public void Patient_Controlled_CreatesPendingApprovalRequest()
    {
        var w = new TestWorld();
        w.AddRx(controlled: true);

        var request = w.RequestOk();

        request.Status.Should().Be(RefillStatus.PendingApproval);
    }

    [Fact]
    public void Request_DoesNotChangeThePrescription()
    {
        var w = new TestWorld();
        var rx = w.AddRx();

        w.RequestOk();

        w.Rx1.Should().Be(rx);
    }

    [Fact]
    public void Caregiver_WithActiveDelegation_CanRequest_AndIsRecordedAsRequester()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Delegate(Caregiver, Patient, DefaultToday.AddDays(-10), DefaultToday.AddDays(10));

        var request = w.RequestOk(requester: Caregiver);

        request.RequestedBy.Should().Be(Caregiver);
        request.PrescriptionId.Should().Be(RxId);
        request.Status.Should().Be(RefillStatus.Requested);
    }

    [Fact]
    public void Caregiver_Controlled_CreatesPendingApprovalRequest()
    {
        var w = new TestWorld();
        w.AddRx(controlled: true);
        w.Delegate(Caregiver, Patient, DefaultToday, DefaultToday);

        w.RequestOk(requester: Caregiver).Status.Should().Be(RefillStatus.PendingApproval);
    }

    // ---------- NotFound ----------

    [Fact]
    public void UnknownPrescription_IsNotFound_AndCreatesNothing()
    {
        var w = new TestWorld();
        w.AddRx();

        var result = w.Desk.Request(Patient, 42);

        result.Should().Be(new RefillResult(RefillRefusal.NotFound, null));
        result.Ok.Should().BeFalse();
        w.Store.RequestsFor(42).Should().BeEmpty();
        w.Store.RequestsFor(RxId).Should().BeEmpty();
    }

    // ---------- who may ask ----------

    [Fact]
    public void Stranger_IsRefused()
    {
        var w = new TestWorld();
        w.AddRx();

        var result = w.Desk.Request(Stranger, RxId);

        result.Should().Be(new RefillResult(RefillRefusal.NotYourPrescription, null));
        result.Ok.Should().BeFalse();
        w.Store.RequestsFor(RxId).Should().BeEmpty();
    }

    [Fact]
    public void AnotherPatient_IsRefused()
    {
        var w = new TestWorld();
        w.AddRx();
        w.AddRx(id: 2, patientId: OtherPatient);

        w.Desk.Request(OtherPatient, RxId).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
        w.Desk.Request(Patient, 2).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Caregiver_DelegatedForADifferentPatient_IsRefused()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Delegate(Caregiver, OtherPatient, DefaultToday.AddDays(-10), DefaultToday.AddDays(10));

        w.Desk.Request(Caregiver, RxId).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Delegation_OfSomeoneElse_DoesNotHelpTheRequester()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Delegate(Stranger, Patient, DefaultToday.AddDays(-10), DefaultToday.AddDays(10));

        w.Desk.Request(Caregiver, RxId).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Delegation_IsDirectional_PatientCannotActForCaregiver()
    {
        var w = new TestWorld();
        w.AddRx(id: 2, patientId: Caregiver);
        w.Delegate(Caregiver, Patient, DefaultToday.AddDays(-10), DefaultToday.AddDays(10));

        w.Desk.Request(Patient, 2).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Theory]
    [InlineData(0, 0, true)]     // single-day delegation on today
    [InlineData(0, 5, true)]     // starts today (From inclusive)
    [InlineData(-5, 0, true)]    // ends today (To inclusive)
    [InlineData(-5, 5, true)]
    [InlineData(1, 5, false)]    // starts tomorrow
    [InlineData(-5, -1, false)]  // ended yesterday
    [InlineData(5, -5, false)]   // From after To: never active
    public void Delegation_IsActiveFromToInclusive(int fromOffset, int toOffset, bool allowed)
    {
        var w = new TestWorld();
        w.AddRx();
        w.Delegate(Caregiver, Patient, DefaultToday.AddDays(fromOffset), DefaultToday.AddDays(toOffset));

        w.Desk.Request(Caregiver, RxId).Refusal
            .Should().Be(allowed ? RefillRefusal.None : RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Caregiver_AnyOneActiveDelegationAmongSeveral_Suffices()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Delegate(Caregiver, Patient, DefaultToday.AddDays(-30), DefaultToday.AddDays(-20));
        w.Delegate(Caregiver, OtherPatient, DefaultToday.AddDays(-30), DefaultToday.AddDays(30));
        w.Delegate(Caregiver, Patient, DefaultToday.AddDays(-1), DefaultToday.AddDays(1));
        w.Delegate(Caregiver, Patient, DefaultToday.AddDays(20), DefaultToday.AddDays(30));

        w.Desk.Request(Caregiver, RxId).Ok.Should().BeTrue();
    }

    [Fact]
    public void Patient_DoesNotNeedADelegation_EvenIfTheirOwnDelegationsAreExpired()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Delegate(Patient, Patient, DefaultToday.AddDays(-30), DefaultToday.AddDays(-20));

        w.Desk.Request(Patient, RxId).Ok.Should().BeTrue();
    }

    [Fact]
    public void DelegationDates_AreJudgedOnTheRiyadhDate()
    {
        var w = new TestWorld();
        w.AddRx();
        // Delegation only on 2026-11-21 (Riyadh).
        w.Delegate(Caregiver, Patient, new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 21));

        w.SetUtc(2026, 11, 20, 20, 59, 59); // 23:59:59 Riyadh on the 20th
        w.Desk.Request(Caregiver, RxId).Refusal.Should().Be(RefillRefusal.NotYourPrescription);

        w.SetUtc(2026, 11, 20, 21, 0, 0); // 00:00 Riyadh on the 21st
        w.Desk.Request(Caregiver, RxId).Refusal.Should().Be(RefillRefusal.None);
    }

    [Fact]
    public void DelegationLastDay_EndsAtRiyadhMidnight()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Delegate(Caregiver, Patient, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20));

        w.SetUtc(2026, 11, 20, 21, 0, 0); // already the 21st in Riyadh, still the 20th in UTC
        w.Desk.Request(Caregiver, RxId).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    // ---------- expiry ----------

    [Theory]
    [InlineData(1, RefillRefusal.None)]
    [InlineData(0, RefillRefusal.None)]          // the expiry day itself is still fine
    [InlineData(-1, RefillRefusal.Expired)]
    [InlineData(-400, RefillRefusal.Expired)]
    public void Expiry_IsInclusiveOfTheExpiryDay(int expiresOffset, RefillRefusal expected)
    {
        var w = new TestWorld();
        w.AddRx(expiresOn: DefaultToday.AddDays(expiresOffset));

        var result = w.Desk.Request(Patient, RxId);

        result.Refusal.Should().Be(expected);
        if (expected != RefillRefusal.None)
        {
            result.Request.Should().BeNull();
            w.Store.RequestsFor(RxId).Should().BeEmpty();
        }
    }

    [Fact]
    public void Expiry_IsJudgedOnTheRiyadhDate()
    {
        var w = new TestWorld();
        w.AddRx(expiresOn: new DateOnly(2026, 11, 20));

        w.SetUtc(2026, 11, 20, 20, 59, 59);
        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.None);

        var w2 = new TestWorld();
        w2.AddRx(expiresOn: new DateOnly(2026, 11, 20));
        w2.SetUtc(2026, 11, 20, 21, 0, 0);
        w2.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.Expired);
    }

    // ---------- refills left ----------

    [Theory]
    [InlineData(1, RefillRefusal.None)]
    [InlineData(0, RefillRefusal.NoRefillsLeft)]
    [InlineData(-1, RefillRefusal.NoRefillsLeft)]
    public void RefillsLeft_MustBePositive(int refillsLeft, RefillRefusal expected)
    {
        var w = new TestWorld();
        w.AddRx(refillsLeft: refillsLeft);

        var result = w.Desk.Request(Patient, RxId);

        result.Refusal.Should().Be(expected);
        if (expected != RefillRefusal.None)
        {
            result.Request.Should().BeNull();
            w.Store.RequestsFor(RxId).Should().BeEmpty();
        }
    }

    // ---------- refill window ----------

    [Theory]
    // runsOut = LastDispensedOn + DaysSupply; window opens WindowDays (7) before that.
    [InlineData(-8, RefillRefusal.TooEarly)]
    [InlineData(-7, RefillRefusal.None)]   // first day of the window
    [InlineData(-6, RefillRefusal.None)]
    [InlineData(0, RefillRefusal.None)]    // the day it runs out
    [InlineData(1, RefillRefusal.None)]    // already run out
    [InlineData(100, RefillRefusal.None)]  // long overdue
    [InlineData(-30, RefillRefusal.TooEarly)]
    public void Window_OpensSevenDaysBeforeSupplyRunsOut(int todayRelativeToRunsOut, RefillRefusal expected)
    {
        var w = new TestWorld();
        var runsOut = DefaultToday.AddDays(-todayRelativeToRunsOut);
        w.AddRx(lastDispensedOn: runsOut.AddDays(-30), daysSupply: 30);

        var result = w.Desk.Request(Patient, RxId);

        result.Refusal.Should().Be(expected);
        if (expected != RefillRefusal.None)
        {
            result.Request.Should().BeNull();
            w.Store.RequestsFor(RxId).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(7, 0, RefillRefusal.None)]      // supply of a week: eligible the day it was dispensed
    [InlineData(8, 0, RefillRefusal.TooEarly)]
    [InlineData(8, 1, RefillRefusal.None)]
    [InlineData(90, 82, RefillRefusal.TooEarly)]
    [InlineData(90, 83, RefillRefusal.None)]
    [InlineData(0, 0, RefillRefusal.None)]
    public void Window_DependsOnDaysSupply(int daysSupply, int daysSinceDispensed, RefillRefusal expected)
    {
        var w = new TestWorld();
        w.AddRx(lastDispensedOn: DefaultToday.AddDays(-daysSinceDispensed), daysSupply: daysSupply);

        w.Desk.Request(Patient, RxId).Refusal.Should().Be(expected);
    }

    [Fact]
    public void WindowDays_IsSeven() => Desk.WindowDays.Should().Be(7);

    [Fact]
    public void Window_IsJudgedOnTheRiyadhDate()
    {
        // runs out 2026-11-28, window opens 2026-11-21 (Riyadh)
        var last = new DateOnly(2026, 10, 29);

        var early = new TestWorld();
        early.AddRx(lastDispensedOn: last, daysSupply: 30);
        early.SetUtc(2026, 11, 20, 20, 59, 59);
        early.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.TooEarly);

        early.SetUtc(2026, 11, 20, 21, 0, 0);
        early.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.None);
    }

    // ---------- one open request at a time ----------

    [Theory]
    [InlineData(RefillStatus.Requested)]
    [InlineData(RefillStatus.PendingApproval)]
    public void OpenRequest_BlocksAnother(RefillStatus openStatus)
    {
        var w = new TestWorld();
        var first = w.RequestIn(openStatus);

        var result = w.Desk.Request(Patient, RxId);

        result.Should().Be(new RefillResult(RefillRefusal.AlreadyOpen, null));
        w.Store.RequestsFor(RxId).Should().ContainSingle().Which.Should().BeSameAs(first);
        first.Status.Should().Be(openStatus);
    }

    [Fact]
    public void OpenRequestByPatient_BlocksCaregiver_AndViceVersa()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Delegate(Caregiver, Patient, DefaultToday.AddDays(-1), DefaultToday.AddDays(1));

        w.RequestOk(requester: Patient);
        w.Desk.Request(Caregiver, RxId).Refusal.Should().Be(RefillRefusal.AlreadyOpen);

        var w2 = new TestWorld();
        w2.AddRx();
        w2.Delegate(Caregiver, Patient, DefaultToday.AddDays(-1), DefaultToday.AddDays(1));
        w2.RequestOk(requester: Caregiver);
        w2.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.AlreadyOpen);
    }

    [Theory]
    [InlineData(RefillStatus.Rejected)]
    [InlineData(RefillStatus.Cancelled)]
    public void ClosedRequest_DoesNotBlockANewOne(RefillStatus closed)
    {
        var w = new TestWorld();
        var first = w.RequestIn(closed);

        var second = w.RequestOk();

        second.Id.Should().NotBe(first.Id);
        first.Status.Should().Be(closed);
        w.Store.RequestsFor(RxId).Should().HaveCount(2);
    }

    [Fact]
    public void ApprovedButNotDispensedRequest_DoesNotBlockANewOne()
    {
        // Current behaviour: only Requested and PendingApproval count as "open". See report.md.
        var w = new TestWorld();
        var approved = w.RequestIn(RefillStatus.Approved);

        var second = w.Desk.Request(Patient, RxId);

        second.Ok.Should().BeTrue();
        second.Request!.Status.Should().Be(RefillStatus.Requested);
        approved.Status.Should().Be(RefillStatus.Approved);
    }

    [Fact]
    public void AfterDispensing_TheWindowRestartsFromTheDispenseDate()
    {
        var w = new TestWorld();
        w.RequestIn(RefillStatus.Dispensed);

        // dispensed today with 30 days supply: window opens today + 23
        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.TooEarly);
        w.SetToday(DefaultToday.AddDays(22));
        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.TooEarly);
        w.SetToday(DefaultToday.AddDays(23));
        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.None);
    }

    [Fact]
    public void AfterDispensing_ShortSupply_CanRequestAgainRightAway()
    {
        var w = new TestWorld();
        w.AddRx(daysSupply: 7);
        var r = w.RequestOk();
        w.MoveOk(r.Id, RefillStatus.Approved);
        w.MoveOk(r.Id, RefillStatus.Dispensed);

        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.None);
    }

    [Fact]
    public void OpenRequestOnOnePrescription_DoesNotBlockAnother()
    {
        var w = new TestWorld();
        w.AddRx(id: 1);
        w.AddRx(id: 2);

        var a = w.RequestOk(rxId: 1);
        var b = w.RequestOk(rxId: 2);

        a.PrescriptionId.Should().Be(1);
        b.PrescriptionId.Should().Be(2);
        w.Store.RequestsFor(1).Should().ContainSingle();
        w.Store.RequestsFor(2).Should().ContainSingle();
    }

    [Fact]
    public void RefusedRequest_DoesNotCountAsOpen()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Desk.Request(Stranger, RxId).Ok.Should().BeFalse();

        w.Desk.Request(Patient, RxId).Ok.Should().BeTrue();
    }

    // ---------- precedence of refusals ----------

    [Fact]
    public void Precedence_NotFound_BeforeEverything()
    {
        var w = new TestWorld();
        w.Desk.Request(Stranger, 123).Refusal.Should().Be(RefillRefusal.NotFound);
    }

    [Fact]
    public void Precedence_NotYourPrescription_BeforeExpiredNoRefillsTooEarlyAndOpen()
    {
        var w = new TestWorld();
        w.AddRx(refillsLeft: 1);
        w.RequestOk(); // an open request exists
        w.Store.Save(Rx(refillsLeft: 0, expiresOn: DefaultToday.AddDays(-1), lastDispensedOn: DefaultToday));

        w.Desk.Request(Stranger, RxId).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    [Fact]
    public void Precedence_Expired_BeforeNoRefillsTooEarlyAndOpen()
    {
        var w = new TestWorld();
        w.AddRx();
        w.RequestOk();
        w.Store.Save(Rx(refillsLeft: 0, expiresOn: DefaultToday.AddDays(-1), lastDispensedOn: DefaultToday));

        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.Expired);
    }

    [Fact]
    public void Precedence_NoRefillsLeft_BeforeTooEarlyAndOpen()
    {
        var w = new TestWorld();
        w.AddRx();
        w.RequestOk();
        w.Store.Save(Rx(refillsLeft: 0, lastDispensedOn: DefaultToday));

        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.NoRefillsLeft);
    }

    [Fact]
    public void Precedence_TooEarly_BeforeOpen()
    {
        var w = new TestWorld();
        w.AddRx();
        w.RequestOk();
        w.Store.Save(Rx(lastDispensedOn: DefaultToday));

        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.TooEarly);
    }

    [Fact]
    public void Precedence_CaregiverWithLapsedDelegation_GetsNotYourPrescription_EvenWhenExpired()
    {
        var w = new TestWorld();
        w.AddRx(expiresOn: DefaultToday.AddDays(-1));
        w.Delegate(Caregiver, Patient, DefaultToday.AddDays(-10), DefaultToday.AddDays(-1));

        w.Desk.Request(Caregiver, RxId).Refusal.Should().Be(RefillRefusal.NotYourPrescription);
    }

    // ---------- reads current store state ----------

    [Fact]
    public void Request_UsesTheCurrentPrescriptionInTheStore()
    {
        var w = new TestWorld();
        w.AddRx();
        w.Store.Save(Rx(refillsLeft: 0));

        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.NoRefillsLeft);

        w.Store.Save(Rx(refillsLeft: 2));
        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.None);
    }

    [Fact]
    public void Request_AfterTimeMovesPastExpiry_IsExpired()
    {
        var w = new TestWorld();
        w.AddRx(expiresOn: DefaultToday);
        w.Desk.Request(Stranger, RxId); // nothing
        w.Time.Advance(TimeSpan.FromDays(1));

        w.Desk.Request(Patient, RxId).Refusal.Should().Be(RefillRefusal.Expired);
    }
}
