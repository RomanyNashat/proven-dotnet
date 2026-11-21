using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

/// <summary>
/// The signed-in user who wants what isn't theirs. Each test names its line in abuse.md.
/// Made-up people: guardian 100 with dependent 200 and former dependent 201 (link ended yesterday),
/// stranger 300, another family 400 with dependent 500. Clinic 7, a second clinic 8.
/// </summary>
public sealed class AbuseTests
{
    private const int Guardian = 100, Dependent = 200, FormerDependent = 201, Stranger = 300,
        OtherGuardian = 400, OtherDependent = 500;
    private const int Clinic = 7, OtherClinic = 8;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryVisitStore _store = new();

    public AbuseTests()
    {
        var now = _time.GetUtcNow();
        _store.Add(new GuardianLink(Guardian, Dependent, Until: null));
        _store.Add(new GuardianLink(Guardian, FormerDependent, Until: now.AddDays(-1)));
        _store.Add(new GuardianLink(OtherGuardian, OtherDependent, Until: null));

        _store.Add(new Promo("SAVE20", 20, ClinicId: null, ValidUntil: null));
        _store.Add(new Promo("HALF", 50, ClinicId: Clinic, ValidUntil: null));
        _store.Add(new Promo("CLINIC8ONLY", 50, ClinicId: OtherClinic, ValidUntil: null));
        _store.Add(new Promo("EXPIRED", 50, ClinicId: null, ValidUntil: now.AddDays(-1)));
    }

    private Desk Desk(IVisitStore? store = null) => new(store ?? _store, _time);

    private Slot AddSlot(int id, decimal price = 300m, int clinic = Clinic, TimeSpan? startsIn = null)
    {
        var slot = new Slot(id, clinic, _time.GetUtcNow().Add(startsIn ?? TimeSpan.FromDays(1)), price);
        _store.Add(slot);
        return slot;
    }

    private static BookRequest Request(int patientId, int slotId, decimal shownPrice = 300m,
        string? promo = null, string? note = null) => new(patientId, slotId, shownPrice, promo, note);

    // ---------------------------------------------------------------- thief

    [Fact, Trait("AB", "AB1")]
    public void Get_AnotherPatientsVisitId_ReturnsNullLikeAMissingId()
    {
        AddSlot(1);
        var theirs = _store.AddVisit(Stranger, 1, Stranger, 300m, null, "private symptoms");

        var desk = Desk();

        desk.Get(Guardian, theirs.Id).Should().BeNull("a changed id must not show someone else's visit");
        desk.Get(Guardian, 999).Should().BeNull();
        desk.Get(Stranger, theirs.Id).Should().NotBeNull("the owner still sees it, so null above is a refusal");
    }

    [Fact, Trait("AB", "AB1")]
    public void Get_OtherFamilysDependentVisit_ReturnsNull()
    {
        AddSlot(1);
        var theirs = _store.AddVisit(OtherDependent, 1, OtherGuardian, 300m, null, "");

        Desk().Get(Guardian, theirs.Id).Should().BeNull();
    }

    [Fact, Trait("AB", "AB2")]
    public void ForPatient_StrangerOrOwnGuardian_ReturnsEmpty()
    {
        AddSlot(1);
        AddSlot(2);
        AddSlot(3);
        _store.AddVisit(Stranger, 1, Stranger, 300m, null, "");
        _store.AddVisit(Guardian, 2, Guardian, 300m, null, "");
        _store.AddVisit(OtherDependent, 3, OtherGuardian, 300m, null, "");

        var desk = Desk();

        desk.ForPatient(Guardian, Stranger).Should().BeEmpty();
        desk.ForPatient(Guardian, OtherDependent).Should().BeEmpty();
        desk.ForPatient(Dependent, Guardian).Should().BeEmpty("the link runs guardian to dependent, not back");
        desk.ForPatient(Stranger, Stranger).Should().ContainSingle("the owner sees their own, so empty above is a refusal");
    }

    [Fact, Trait("AB", "AB3")]
    public void GetForPatientCancel_FormerGuardianAfterLinkEnded_SeesAndCancelsNothing()
    {
        AddSlot(1);
        // Booked by the former dependent themselves, after the link ended: the former guardian has no claim on
        // it. (A visit the guardian booked and paid for before the link ended is story.md Q8, not tested here.)
        var visit = _store.AddVisit(FormerDependent, 1, FormerDependent, 300m, null, "");

        var desk = Desk();

        desk.Get(Guardian, visit.Id).Should().BeNull();
        desk.ForPatient(Guardian, FormerDependent).Should().BeEmpty();
        desk.Cancel(Guardian, visit.Id).Should().BeFalse();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
        _store.Refunds.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- vandal: someone else's visit

    [Fact, Trait("AB", "AB4")]
    public void Cancel_AnotherPatientsVisit_ReturnsFalseKeepsItBookedNoRefund()
    {
        AddSlot(1);
        AddSlot(2);
        var stranger = _store.AddVisit(Stranger, 1, Stranger, 300m, null, "");
        var otherFamily = _store.AddVisit(OtherDependent, 2, OtherGuardian, 300m, null, "");

        var desk = Desk();

        desk.Cancel(Guardian, stranger.Id).Should().BeFalse();
        desk.Cancel(Guardian, otherFamily.Id).Should().BeFalse();
        _store.FindVisit(stranger.Id)!.Status.Should().Be(VisitStatus.Booked);
        _store.FindVisit(otherFamily.Id)!.Status.Should().Be(VisitStatus.Booked);
        _store.Refunds.Should().BeEmpty();
        desk.FreeSlots(Clinic).Should().BeEmpty("both slots are still taken");
    }

    // ---------------------------------------------------------------- forger

    [Theory, Trait("AB", "AB5")]
    [InlineData(Stranger)]
    [InlineData(OtherDependent)]
    [InlineData(FormerDependent)]
    public void Book_ForSomeoneNotTheirDependent_RefusedNotYourPatient(int patientId)
    {
        AddSlot(1);

        var result = Desk().Book(Guardian, Request(patientId, 1));

        result.Ok.Should().BeFalse();
        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Fact, Trait("AB", "AB5")]
    public void Book_DependentBooksForTheirGuardian_RefusedNotYourPatient()
    {
        AddSlot(1);

        var result = Desk().Book(Dependent, Request(Guardian, 1));

        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Theory, Trait("AB", "AB6")]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-300)]
    public void Book_ShownPriceBelowSlotPrice_ChargesSlotPrice(int shownPrice)
    {
        AddSlot(1, price: 300m);

        var result = Desk().Book(Guardian, Request(Guardian, 1, shownPrice: shownPrice));

        result.Ok.Should().BeTrue();
        result.Visit!.Charged.Should().Be(300m);
        _store.VisitsOf(Guardian).Should().ContainSingle().Which.Charged.Should().Be(300m);
    }

    [Fact, Trait("AB", "AB6")]
    public void Book_SlotPriceRaisedAfterAppShowedIt_ChargesTodaysPrice()
    {
        AddSlot(1, price: 300m);
        _store.Add(new Slot(1, Clinic, _time.GetUtcNow().AddDays(1), 450m)); // the clinic changes the price

        var result = Desk().Book(Guardian, Request(Guardian, 1, shownPrice: 300m));

        result.Visit!.Charged.Should().Be(450m, "Slot.Price is what the clinic charges today; ShownPrice is for the receipt");
    }

    [Theory, Trait("AB", "AB7")]
    [InlineData("CLINIC8ONLY")]
    [InlineData("EXPIRED")]
    public void Book_PromoForOtherClinicOrExpired_RefusedNoVisit(string code)
    {
        AddSlot(1, price: 300m, clinic: Clinic);

        var result = Desk().Book(Guardian, Request(Guardian, 1, promo: code));

        result.Ok.Should().BeFalse("clinic 7 doesn't have this code today");
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Fact, Trait("AB", "AB7")]
    public void Book_PromoForOtherClinic_RefusedAsUnknownPromo()
    {
        AddSlot(1, clinic: Clinic);

        var result = Desk().Book(Guardian, Request(Guardian, 1, promo: "CLINIC8ONLY"));

        result.Refusal.Should().Be(BookRefusal.UnknownPromo, "the request: a code the clinic doesn't have refuses the booking");
    }

    // ---------------------------------------------------------------- cheater

    [Theory, Trait("AB", "AB8")]
    [InlineData("SAVE20")]
    [InlineData("save20")]
    [InlineData("  Save20 ")]
    public void Book_SamePromoSecondTime_RefusedPromoUsed(string secondSpelling)
    {
        AddSlot(1);
        AddSlot(2);
        var desk = Desk();
        desk.Book(Guardian, Request(Guardian, 1, promo: "SAVE20")).Ok.Should().BeTrue();

        var second = desk.Book(Guardian, Request(Guardian, 2, promo: secondSpelling));

        second.Refusal.Should().Be(BookRefusal.PromoUsed);
        _store.VisitsOf(Guardian).Should().ContainSingle();
    }

    [Fact, Trait("AB", "AB9")]
    public void Book_ManyParallelBookingsWithOnePromo_OnlyOneGetsTheDiscount()
    {
        const int attempts = 24;
        for (var i = 1; i <= attempts; i++)
            AddSlot(i, price: 300m);
        var desk = Desk(new SlowStore(_store));

        var results = Race(attempts, i => desk.Book(Guardian, Request(Guardian, i + 1, promo: "SAVE20")));

        results.Count(r => r.Ok).Should().Be(1, "the promo is once per patient, even when the app fires in parallel");
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.PromoUsed);
        _store.VisitsOf(Guardian).Count(v => v.PromoCode != null).Should().Be(1);
    }

    [Fact, Trait("AB", "AB10")]
    public void Cancel_SameVisitTwice_OneRefundOnly()
    {
        AddSlot(1);
        var visit = _store.AddVisit(Guardian, 1, Guardian, 300m, null, "");
        var desk = Desk();

        desk.Cancel(Guardian, visit.Id).Should().BeTrue();
        desk.Cancel(Guardian, visit.Id).Should().BeFalse();

        _store.Refunds.Should().ContainSingle().Which.Should().Be((visit.Id, 300m));
    }

    [Theory, Trait("AB", "AB10")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(36_000_000_000)]
    // the moment it starts, one tick after, an hour after
    public void Cancel_AtOrAfterStart_ReturnsFalseNoRefund(long ticksAfterStart)
    {
        var slot = AddSlot(1, startsIn: TimeSpan.FromHours(2));
        var visit = _store.AddVisit(Guardian, 1, Guardian, 300m, null, "");
        var desk = Desk();
        _time.SetUtcNow(slot.StartsAt.AddTicks(ticksAfterStart));

        desk.Cancel(Guardian, visit.Id).Should().BeFalse();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
        _store.Refunds.Should().BeEmpty();
    }

    [Fact, Trait("AB", "AB11")]
    public void Cancel_AfterPromoAndSlotPriceRaised_RefundsWhatWasCharged()
    {
        AddSlot(1, price: 300m);
        var desk = Desk();
        var booked = desk.Book(Guardian, Request(Guardian, 1, promo: "HALF"));
        booked.Visit!.Charged.Should().Be(150m);
        _store.Add(new Slot(1, Clinic, _time.GetUtcNow().AddDays(1), 900m)); // the price goes up afterwards

        desk.Cancel(Guardian, booked.Visit.Id).Should().BeTrue();

        _store.Refunds.Should().ContainSingle().Which.Amount.Should().Be(150m);
    }

    [Fact, Trait("AB", "AB12")]
    public void Book_ManyPatientsSameSlotAtOnce_ExactlyOneBooked()
    {
        const int patients = 24;
        AddSlot(1);
        var desk = Desk(new SlowStore(_store));

        var results = Race(patients, i => desk.Book(1000 + i, Request(1000 + i, 1)));

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.SlotTaken);
        _store.VisitsForSlot(1).Count(v => v.Status == VisitStatus.Booked).Should().Be(1);
    }

    [Fact, Trait("AB", "AB12")]
    public void Book_SlotAlreadyBookedBySomeoneElse_RefusedSlotTaken()
    {
        AddSlot(1);
        var desk = Desk();
        desk.Book(Stranger, Request(Stranger, 1)).Ok.Should().BeTrue();

        var second = desk.Book(Guardian, Request(Guardian, 1));

        second.Refusal.Should().Be(BookRefusal.SlotTaken);
        _store.VisitsForSlot(1).Should().ContainSingle().Which.PatientId.Should().Be(Stranger);
    }

    [Fact, Trait("AB", "AB13")]
    public void Cancel_ManyParallelCancelsOfOneVisit_OneRefund()
    {
        const int attempts = 24;
        AddSlot(1);
        var visit = _store.AddVisit(Guardian, 1, Guardian, 300m, null, "");
        var desk = Desk(new SlowStore(_store));

        var results = Race(attempts, _ => desk.Cancel(Guardian, visit.Id));

        results.Count(ok => ok).Should().Be(1);
        _store.Refunds.Should().ContainSingle().Which.Should().Be((visit.Id, 300m));
    }

    [Theory, Trait("AB", "AB14")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-36_000_000_000)]
    // starts right now, started a tick ago, started an hour ago
    public void Book_SlotAlreadyStarted_RefusedSlotInPast(long startsInTicks)
    {
        AddSlot(1, startsIn: TimeSpan.FromTicks(startsInTicks));

        var result = Desk().Book(Guardian, Request(Guardian, 1));

        result.Refusal.Should().Be(BookRefusal.SlotInPast);
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- vandal

    [Theory, Trait("AB", "AB15")]
    [InlineData(VisitDesk.Desk.MaxNoteLength + 1)]
    [InlineData(1_000_000)]
    public void Book_NoteOverLimit_RefusedNoteTooLongNothingStored(int length)
    {
        AddSlot(1);

        var result = Desk().Book(Guardian, Request(Guardian, 1, note: new string('x', length)));

        result.Refusal.Should().Be(BookRefusal.NoteTooLong);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    // ---------------------------------------------------------------- racing helpers

    /// <summary>Runs <paramref name="count"/> calls on their own threads, released together.</summary>
    private static List<T> Race<T>(int count, Func<int, T> call)
    {
        var results = new ConcurrentBag<T>();
        var errors = new ConcurrentBag<Exception>();
        using var start = new Barrier(count);
        var threads = Enumerable.Range(0, count).Select(i => new Thread(() =>
        {
            start.SignalAndWait();
            try { results.Add(call(i)); }
            catch (Exception e) { errors.Add(e); }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        if (!errors.IsEmpty)
            throw new AggregateException(errors);
        return [.. results];
    }

    /// <summary>
    /// The real store, with a pause on every read, the way a database round trip has one. It widens the gap
    /// between a check and the write that follows it, so a check-then-act without a lock fails every run.
    /// </summary>
    private sealed class SlowStore(InMemoryVisitStore inner) : IVisitStore
    {
        private static T Slow<T>(Func<T> read)
        {
            var value = read();
            Thread.Sleep(5);
            return value;
        }

        public Slot? FindSlot(int id) => Slow(() => inner.FindSlot(id));
        public IReadOnlyList<Slot> SlotsOf(int clinicId) => Slow(() => inner.SlotsOf(clinicId));
        public Promo? FindPromo(string code) => Slow(() => inner.FindPromo(code));
        public IReadOnlyList<GuardianLink> LinksOf(int guardianId) => Slow(() => inner.LinksOf(guardianId));
        public Visit? FindVisit(int id) => Slow(() => inner.FindVisit(id));
        public IReadOnlyList<Visit> VisitsOf(int patientId) => Slow(() => inner.VisitsOf(patientId));
        public IReadOnlyList<Visit> VisitsForSlot(int slotId) => Slow(() => inner.VisitsForSlot(slotId));
        public Visit AddVisit(int patientId, int slotId, int bookedBy, decimal charged, string? promoCode, string note)
            => inner.AddVisit(patientId, slotId, bookedBy, charged, promoCode, note);
        public void Save(Visit visit) => inner.Save(visit);
        public void Refund(int visitId, decimal amount) => inner.Refund(visitId, amount);
    }
}
