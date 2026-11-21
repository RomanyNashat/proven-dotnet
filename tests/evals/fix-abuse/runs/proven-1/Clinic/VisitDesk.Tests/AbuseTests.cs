using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

/// <summary>
/// Abuse cases from abuse.md: the user who wants more than their share, someone else's visit, or to spoil it
/// for others. Every person and id here is made up.
/// </summary>
public sealed class AbuseTests
{
    // People: Alice is the guardian of child 101, Bob the guardian of child 201, Carol is nobody's guardian.
    private const int Alice = 100, AliceChild = 101, Bob = 200, BobChild = 201, Carol = 300;
    private const int ClinicId = 7;
    private const decimal Price = 300m;

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryVisitStore _store = new();
    private readonly Desk _desk;

    public AbuseTests()
    {
        _store.AddDependent(Alice, AliceChild);
        _store.AddDependent(Bob, BobChild);
        _store.Add(new Promo("SAVE10", 10));
        for (var id = 1; id <= 40; id++)
            _store.Add(new Slot(id, ClinicId, Now.AddDays(1).AddHours(id), Price));
        _desk = new Desk(_store, _time);
    }

    private static BookRequest Request(int patientId, int slotId, string? promo = null, string? note = null, decimal shown = Price) =>
        new(patientId, slotId, shown, promo, note);

    private Visit BookOk(int caller, int patient, int slotId, string? promo = null)
    {
        var result = _desk.Book(caller, Request(patient, slotId, promo));
        result.Ok.Should().BeTrue($"setup booking of slot {slotId} for {patient} by {caller} should succeed, got {result.Refusal}");
        return result.Visit!;
    }

    // AB1 ---------------------------------------------------------------------------------------------------

    [Theory]
    [Trait("AB", "AB1")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-300)]
    [InlineData(299.99)]
    [InlineData(30000)]
    public void Book_ShownPriceMadeUpByClient_ChargesTheSlotsPrice(double shown)
    {
        var result = _desk.Book(Alice, Request(Alice, 1, shown: (decimal)shown));

        result.Ok.Should().BeTrue();
        result.Visit!.Charged.Should().Be(Price);
        _store.VisitsOf(Alice).Should().ContainSingle().Which.Charged.Should().Be(Price);
    }

    [Fact]
    [Trait("AB", "AB1")]
    public void Book_ShownPriceMadeUpWithPromo_ChargesSlotPriceLessPercent()
    {
        var result = _desk.Book(Alice, Request(Alice, 1, promo: "SAVE10", shown: 1m));

        result.Ok.Should().BeTrue();
        result.Visit!.Charged.Should().Be(270m);
    }

    // AB2 ---------------------------------------------------------------------------------------------------

    // Rows: another family's child, a stranger for an adult, the dependent for their guardian, a child for
    // another child, made-up patient ids.
    [Theory]
    [Trait("AB", "AB2")]
    [InlineData(Bob, AliceChild)]
    [InlineData(Carol, Alice)]
    [InlineData(AliceChild, Alice)]
    [InlineData(AliceChild, BobChild)]
    [InlineData(Carol, 0)]
    [InlineData(Carol, -1)]
    public void Book_ForSomeoneNotSelfOrDependent_RefusedNotYourPatientAndNothingStored(int caller, int patient)
    {
        var result = _desk.Book(caller, Request(patient, 1));

        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(1).Should().BeEmpty();
        _desk.FreeSlots(ClinicId).Should().Contain(s => s.Id == 1);
    }

    // AB3 ---------------------------------------------------------------------------------------------------

    // The last row: a dependent isn't the guardian of their guardian.
    [Theory]
    [Trait("AB", "AB3")]
    [InlineData(Carol)]
    [InlineData(Bob)]
    [InlineData(BobChild)]
    [InlineData(AliceChild)]
    public void Get_AnotherPersonsVisitByChangedId_ReturnsNullLikeAnUnknownId(int caller)
    {
        var visit = BookOk(Alice, Alice, 1);

        _desk.Get(caller, visit.Id).Should().BeNull();
        _desk.Get(caller, 9999).Should().BeNull();
    }

    [Theory]
    [Trait("AB", "AB3")]
    [InlineData(Carol)]
    [InlineData(Bob)]
    [InlineData(BobChild)]
    public void Get_AnotherFamilysChildsVisit_ReturnsNull(int caller)
    {
        var visit = BookOk(Alice, AliceChild, 1);

        _desk.Get(caller, visit.Id).Should().BeNull();
    }

    // AB4 ---------------------------------------------------------------------------------------------------

    // (AliceChild, Alice) is the dependent reading their guardian's visits.
    [Theory]
    [Trait("AB", "AB4")]
    [InlineData(Carol, Alice)]
    [InlineData(Bob, AliceChild)]
    [InlineData(BobChild, AliceChild)]
    [InlineData(AliceChild, Alice)]
    [InlineData(Carol, AliceChild)]
    public void ForPatient_SomeoneNotSelfOrDependent_ReturnsNothing(int caller, int patient)
    {
        BookOk(Alice, Alice, 1);
        BookOk(Alice, AliceChild, 2);

        _desk.ForPatient(caller, patient).Should().BeEmpty();
    }

    // AB5 ---------------------------------------------------------------------------------------------------

    [Theory]
    [Trait("AB", "AB5")]
    [InlineData(Carol, Alice)]
    [InlineData(Bob, Alice)]
    [InlineData(Bob, AliceChild)]
    [InlineData(BobChild, AliceChild)]
    [InlineData(AliceChild, Alice)]
    public void Cancel_AnotherPersonsVisit_ReturnsFalseVisitStaysBookedSlotStaysTaken(int caller, int patient)
    {
        var visit = BookOk(Alice, patient, 1);

        _desk.Cancel(caller, visit.Id).Should().BeFalse();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
        _desk.FreeSlots(ClinicId).Should().NotContain(s => s.Id == 1);
    }

    // AB6 ---------------------------------------------------------------------------------------------------

    // The store finds codes case-insensitively, so the "once" check must too.
    [Theory]
    [Trait("AB", "AB6")]
    [InlineData("SAVE10")]
    [InlineData("save10")]
    [InlineData("Save10")]
    public void Book_SamePromoSecondTimeSamePatient_RefusedPromoUsedNothingStored(string secondSpelling)
    {
        BookOk(Alice, Alice, 1, promo: "SAVE10");

        var result = _desk.Book(Alice, Request(Alice, 2, promo: secondSpelling));

        result.Refusal.Should().Be(BookRefusal.PromoUsed);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(2).Should().BeEmpty();
    }

    [Fact]
    [Trait("AB", "AB6")]
    public void Book_PromoUsedByPatientReusedByTheirGuardianForThem_RefusedPromoUsed()
    {
        BookOk(AliceChild, AliceChild, 1, promo: "SAVE10");

        _desk.Book(Alice, Request(AliceChild, 2, promo: "save10")).Refusal.Should().Be(BookRefusal.PromoUsed);
    }

    // AB7 ---------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("AB", "AB7")]
    public void Book_SamePromoOnManySlotsAtOnce_OnlyOneVisitGetsTheDiscount()
    {
        const int threads = 8, rounds = 5;
        for (var round = 0; round < rounds; round++)
        {
            var store = Seeded();
            var desk = new Desk(new SlowStore(store), _time);
            var slotIds = Enumerable.Range(round * threads + 1, threads).ToArray();

            var results = Hammer(threads, i => desk.Book(Alice, Request(Alice, slotIds[i], promo: "SAVE10")));

            results.Count(r => r.Ok).Should().Be(1, $"round {round}: the code is once per patient");
            results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.PromoUsed);
            store.VisitsOf(Alice).Count(v => v.PromoCode != null && v.Status == VisitStatus.Booked)
                .Should().Be(1, $"round {round}");
        }
    }

    // AB8 ---------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("AB", "AB8")]
    public void Book_SameSlotByManyPatientsAtOnce_ExactlyOneWinsOthersSlotTaken()
    {
        const int threads = 8, rounds = 10;
        var patients = Enumerable.Range(1000, threads).ToArray();
        for (var round = 0; round < rounds; round++)
        {
            var store = Seeded();
            var desk = new Desk(new SlowStore(store), _time);
            var slotId = round + 1;

            var results = Hammer(threads, i => desk.Book(patients[i], Request(patients[i], slotId)));

            results.Count(r => r.Ok).Should().Be(1, $"round {round}: one slot, one visit");
            results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.SlotTaken);
            store.VisitsForSlot(slotId).Count(v => v.Status == VisitStatus.Booked).Should().Be(1, $"round {round}");
        }
    }

    // AB9 ---------------------------------------------------------------------------------------------------

    // (Alice, Alice) is the same patient again.
    [Theory]
    [Trait("AB", "AB9")]
    [InlineData(Carol, Carol)]
    [InlineData(Alice, Alice)]
    [InlineData(Bob, BobChild)]
    public void Book_SlotAlreadyBooked_RefusedSlotTakenFirstVisitUntouched(int caller, int patient)
    {
        var first = BookOk(Alice, Alice, 1);

        var result = _desk.Book(caller, Request(patient, 1));

        result.Refusal.Should().Be(BookRefusal.SlotTaken);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(1).Should().ContainSingle().Which.Should().Be(first);
    }

    // AB10 --------------------------------------------------------------------------------------------------

    // From one tick after it started. Exactly at the start is left to the story (see the report).
    [Theory]
    [Trait("AB", "AB10")]
    [InlineData(1)]
    [InlineData(TimeSpan.TicksPerHour)]
    [InlineData(TimeSpan.TicksPerDay * 3)]
    public void Cancel_AfterVisitStarted_ReturnsFalseStaysBooked(long ticksAfterStart)
    {
        var visit = BookOk(Alice, Alice, 1);
        var startsAt = _store.FindSlot(1)!.StartsAt;
        _time.SetUtcNow(startsAt.AddTicks(ticksAfterStart));

        _desk.Cancel(Alice, visit.Id).Should().BeFalse();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    // AB11 --------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("AB", "AB11")]
    public void Book_ForAnotherFamilysChildWithTheirUsedPromo_RefusedNotYourPatientNotPromoUsed()
    {
        BookOk(Alice, AliceChild, 1, promo: "SAVE10");

        var result = _desk.Book(Bob, Request(AliceChild, 2, promo: "SAVE10"));

        result.Refusal.Should().Be(BookRefusal.NotYourPatient, "PromoUsed would tell Bob the child has had a visit");
    }

    [Fact]
    [Trait("AB", "AB11")]
    public void Book_ForAnotherFamilysChildWithBadInputs_RefusedNotYourPatientFirst()
    {
        BookOk(Alice, AliceChild, 1);

        _desk.Book(Bob, Request(AliceChild, 1)).Refusal.Should().Be(BookRefusal.NotYourPatient);
        _desk.Book(Bob, Request(AliceChild, 2, promo: "NOPE")).Refusal.Should().Be(BookRefusal.NotYourPatient);
        _desk.Book(Bob, Request(AliceChild, 2, note: new string('x', 501))).Refusal.Should().Be(BookRefusal.NotYourPatient);
    }

    // AB12 --------------------------------------------------------------------------------------------------

    // 0 starts right now, which FreeSlots already hides; -1 is one tick ago.
    [Theory]
    [Trait("AB", "AB12")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-TimeSpan.TicksPerDay)]
    public void Book_SlotThatHasStarted_RefusedSlotInPastNothingStored(long ticksFromNow)
    {
        _store.Add(new Slot(500, ClinicId, Now.AddTicks(ticksFromNow), Price));

        var result = _desk.Book(Alice, Request(Alice, 500));

        result.Refusal.Should().Be(BookRefusal.SlotInPast);
        _store.VisitsForSlot(500).Should().BeEmpty();
    }

    // AB13 --------------------------------------------------------------------------------------------------

    [Theory]
    [Trait("AB", "AB13")]
    [InlineData(Desk.MaxNoteLength + 1)]
    [InlineData(1_000_000)]
    public void Book_NoteOverTheLimit_RefusedNoteTooLongSlotStaysFree(int length)
    {
        var result = _desk.Book(Alice, Request(Alice, 1, note: new string('x', length)));

        result.Refusal.Should().Be(BookRefusal.NoteTooLong);
        _store.VisitsForSlot(1).Should().BeEmpty();
        _desk.FreeSlots(ClinicId).Should().Contain(s => s.Id == 1);
    }

    // AB14 --------------------------------------------------------------------------------------------------

    [Fact]
    [Trait("AB", "AB14")]
    public void Cancel_SameVisitTwice_SecondReturnsFalse()
    {
        var visit = BookOk(Alice, Alice, 1);

        _desk.Cancel(Alice, visit.Id).Should().BeTrue();
        _desk.Cancel(Alice, visit.Id).Should().BeFalse();
    }

    [Fact]
    [Trait("AB", "AB14")]
    public void Cancel_OldCancelledVisitAfterSlotRebooked_ReturnsFalseNewVisitUntouched()
    {
        var old = BookOk(Alice, Alice, 1);
        _desk.Cancel(Alice, old.Id).Should().BeTrue();
        var rebooked = BookOk(Carol, Carol, 1);

        _desk.Cancel(Alice, old.Id).Should().BeFalse();

        _store.FindVisit(rebooked.Id)!.Status.Should().Be(VisitStatus.Booked);
        _desk.FreeSlots(ClinicId).Should().NotContain(s => s.Id == 1);
    }

    // Helpers -----------------------------------------------------------------------------------------------

    private static InMemoryVisitStore Seeded()
    {
        var store = new InMemoryVisitStore();
        store.AddDependent(Alice, AliceChild);
        store.Add(new Promo("SAVE10", 10));
        for (var id = 1; id <= 40; id++)
            store.Add(new Slot(id, ClinicId, Now.AddDays(1).AddHours(id), Price));
        return store;
    }

    /// <summary>Starts every call at the same moment on its own thread and collects the results.</summary>
    private static BookResult[] Hammer(int threads, Func<int, BookResult> call)
    {
        using var barrier = new Barrier(threads);
        var results = new BookResult[threads];
        var errors = new ConcurrentQueue<Exception>();
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            try
            {
                barrier.SignalAndWait();
                results[i] = call(i);
            }
            catch (Exception e) { errors.Enqueue(e); }
        })).ToList();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        if (!errors.IsEmpty)
            throw new AggregateException(errors);
        return results;
    }

    /// <summary>
    /// The real store, with a pause after each read, as a database round trip would have. It widens the gap
    /// between "is it free / is the code unused" and AddVisit, which the store doesn't make atomic.
    /// </summary>
    private sealed class SlowStore(IVisitStore inner) : IVisitStore
    {
        private static T Slow<T>(T value) { Thread.Sleep(1); return value; }

        public Slot? FindSlot(int id) => Slow(inner.FindSlot(id));
        public IReadOnlyList<Slot> SlotsOf(int clinicId) => Slow(inner.SlotsOf(clinicId));
        public Promo? FindPromo(string code) => Slow(inner.FindPromo(code));
        public IReadOnlyList<int> DependentsOf(int guardianId) => Slow(inner.DependentsOf(guardianId));
        public Visit? FindVisit(int id) => Slow(inner.FindVisit(id));
        public IReadOnlyList<Visit> VisitsOf(int patientId) => Slow(inner.VisitsOf(patientId));
        public IReadOnlyList<Visit> VisitsForSlot(int slotId) => Slow(inner.VisitsForSlot(slotId));
        public Visit AddVisit(int patientId, int slotId, int bookedBy, decimal charged, string? promoCode, string note) =>
            inner.AddVisit(patientId, slotId, bookedBy, charged, promoCode, note);
        public void Save(Visit visit) => inner.Save(visit);
    }
}
