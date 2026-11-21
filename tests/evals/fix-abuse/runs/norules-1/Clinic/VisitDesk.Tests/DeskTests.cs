using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

public sealed class DeskTests
{
    private const int Clinic = 7;
    private const int Alice = 1;     // guardian of Bob
    private const int Bob = 2;       // Alice's dependent
    private const int Mallory = 3;   // unrelated user

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryVisitStore _store = new();
    private readonly Desk _desk;

    public DeskTests()
    {
        _store.Add(new Slot(1, Clinic, _time.GetUtcNow().AddDays(1), 300m));
        _store.Add(new Slot(2, Clinic, _time.GetUtcNow().AddDays(2), 199.99m));
        _store.Add(new Slot(3, Clinic, _time.GetUtcNow().AddHours(-1), 300m));
        _store.Add(new Promo("SPRING20", 20));
        _store.AddDependent(Alice, Bob);
        _desk = new Desk(_store, _time);
    }

    private static BookRequest Req(int patient, int slot = 1, string? promo = null, string? note = null,
        decimal shown = 300m) => new(patient, slot, shown, promo, note);

    [Fact]
    public void FreeSlots_OneSlotTomorrow_IsListed()
    {
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == 1);
    }

    [Fact]
    public void Book_ForSelf_ChargesSlotPrice()
    {
        var result = _desk.Book(Alice, Req(Alice));

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(Alice);
        result.Visit.BookedBy.Should().Be(Alice);
        result.Visit.Charged.Should().Be(300m);
        result.Visit.Note.Should().Be("");
        result.Visit.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    public void Book_IgnoresPriceShownByApp()
    {
        var result = _desk.Book(Alice, Req(Alice, shown: 1m));

        result.Visit!.Charged.Should().Be(300m);
    }

    [Fact]
    public void Book_ForDependent_ByGuardian_Succeeds()
    {
        var result = _desk.Book(Alice, Req(Bob));

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(Bob);
        result.Visit.BookedBy.Should().Be(Alice);
    }

    [Theory]
    [InlineData(Mallory, Alice)]
    [InlineData(Bob, Alice)]      // dependent is not their guardian's guardian
    [InlineData(Mallory, Bob)]
    public void Book_ForSomeoneElse_IsRefused(int caller, int patient)
    {
        var result = _desk.Book(caller, Req(patient));

        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Fact]
    public void Book_UnknownSlot_IsRefused() =>
        _desk.Book(Alice, Req(Alice, slot: 99)).Refusal.Should().Be(BookRefusal.SlotNotFound);

    [Fact]
    public void Book_PastSlot_IsRefused() =>
        _desk.Book(Alice, Req(Alice, slot: 3)).Refusal.Should().Be(BookRefusal.SlotInPast);

    [Fact]
    public void Book_SlotStartingNow_IsRefused()
    {
        _time.Advance(TimeSpan.FromDays(1));

        _desk.Book(Alice, Req(Alice)).Refusal.Should().Be(BookRefusal.SlotInPast);
    }

    [Fact]
    public void Book_TakenSlot_IsRefused()
    {
        _desk.Book(Alice, Req(Alice)).Ok.Should().BeTrue();

        _desk.Book(Mallory, Req(Mallory)).Refusal.Should().Be(BookRefusal.SlotTaken);
    }

    [Fact]
    public void Book_BookedSlot_LeavesFreeSlots()
    {
        _desk.Book(Alice, Req(Alice));

        _desk.FreeSlots(Clinic).Select(s => s.Id).Should().Equal(2);
    }

    [Fact]
    public void Book_Promo_TakesPercentOff()
    {
        var result = _desk.Book(Alice, Req(Alice, promo: "SPRING20"));

        result.Visit!.Charged.Should().Be(240m);
        result.Visit.PromoCode.Should().Be("SPRING20");
    }

    [Fact]
    public void Book_Promo_RoundsToCents()
    {
        // 199.99 * 0.8 = 159.992
        _desk.Book(Alice, Req(Alice, slot: 2, promo: "SPRING20")).Visit!.Charged.Should().Be(159.99m);
    }

    [Fact]
    public void Book_Promo_IsCaseInsensitiveAndTrimmed()
    {
        var result = _desk.Book(Alice, Req(Alice, promo: "  spring20 "));

        result.Visit!.Charged.Should().Be(240m);
        result.Visit.PromoCode.Should().Be("SPRING20");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Book_BlankPromo_MeansNoPromo(string promo)
    {
        var result = _desk.Book(Alice, Req(Alice, promo: promo));

        result.Ok.Should().BeTrue();
        result.Visit!.Charged.Should().Be(300m);
        result.Visit.PromoCode.Should().BeNull();
    }

    [Fact]
    public void Book_UnknownPromo_IsRefused()
    {
        _desk.Book(Alice, Req(Alice, promo: "FREE100")).Refusal.Should().Be(BookRefusal.UnknownPromo);
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Fact]
    public void Book_PromoTwiceBySamePatient_IsRefused()
    {
        _desk.Book(Alice, Req(Alice, promo: "SPRING20")).Ok.Should().BeTrue();

        _desk.Book(Alice, Req(Alice, slot: 2, promo: "spring20")).Refusal.Should().Be(BookRefusal.PromoUsed);
    }

    [Fact]
    public void Book_PromoIsPerPatient_DependentMayUseItToo()
    {
        _desk.Book(Alice, Req(Alice, promo: "SPRING20")).Ok.Should().BeTrue();

        _desk.Book(Alice, Req(Bob, slot: 2, promo: "SPRING20")).Ok.Should().BeTrue();
    }

    [Fact]
    public void Book_PromoFromCancelledVisit_MayBeUsedAgain()
    {
        var first = _desk.Book(Alice, Req(Alice, promo: "SPRING20")).Visit!;
        _desk.Cancel(Alice, first.Id).Should().BeTrue();

        _desk.Book(Alice, Req(Alice, slot: 2, promo: "SPRING20")).Ok.Should().BeTrue();
    }

    [Fact]
    public void Book_NoteAtLimit_IsKept()
    {
        var note = new string('a', Desk.MaxNoteLength);

        _desk.Book(Alice, Req(Alice, note: note)).Visit!.Note.Should().Be(note);
    }

    [Fact]
    public void Book_NoteOverLimit_IsRefused() =>
        _desk.Book(Alice, Req(Alice, note: new string('a', Desk.MaxNoteLength + 1)))
            .Refusal.Should().Be(BookRefusal.NoteTooLong);

    [Fact]
    public async Task Book_ManyCallersSameSlot_OnlyOneWins()
    {
        var results = await Task.WhenAll(Enumerable.Range(100, 50)
            .Select(user => Task.Run(() => new Desk(_store, _time).Book(user, Req(user)))));

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.SlotTaken);
        _store.VisitsForSlot(1).Should().ContainSingle();
    }

    [Fact]
    public async Task Book_SamePromoConcurrently_UsedOnce()
    {
        for (var id = 10; id < 40; id++)
            _store.Add(new Slot(id, Clinic, _time.GetUtcNow().AddDays(3).AddHours(id), 100m));

        var results = await Task.WhenAll(Enumerable.Range(10, 30)
            .Select(slot => Task.Run(() => _desk.Book(Alice, Req(Alice, slot: slot, promo: "SPRING20")))));

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.PromoUsed);
    }

    [Fact]
    public void Get_OwnVisit_IsReturned()
    {
        var visit = _desk.Book(Alice, Req(Alice)).Visit!;

        _desk.Get(Alice, visit.Id).Should().Be(visit);
    }

    [Fact]
    public void Get_DependentsVisit_ByGuardian_IsReturned()
    {
        var visit = _desk.Book(Alice, Req(Bob)).Visit!;

        _desk.Get(Alice, visit.Id).Should().Be(visit);
        _desk.Get(Bob, visit.Id).Should().Be(visit);
    }

    [Fact]
    public void Get_SomeoneElsesVisit_IsNull()
    {
        var visit = _desk.Book(Alice, Req(Alice)).Visit!;

        _desk.Get(Mallory, visit.Id).Should().BeNull();
        _desk.Get(Bob, visit.Id).Should().BeNull();
    }

    [Fact]
    public void Get_Missing_IsNull() => _desk.Get(Alice, 42).Should().BeNull();

    [Fact]
    public void Cancel_BeforeStart_CancelsAndFreesSlot()
    {
        var visit = _desk.Book(Alice, Req(Alice)).Visit!;

        _desk.Cancel(Alice, visit.Id).Should().BeTrue();

        _desk.Get(Alice, visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == 1);
        _desk.Book(Mallory, Req(Mallory)).Ok.Should().BeTrue();
    }

    [Fact]
    public void Cancel_DependentsVisit_ByGuardian_Succeeds()
    {
        var visit = _desk.Book(Alice, Req(Bob)).Visit!;

        _desk.Cancel(Alice, visit.Id).Should().BeTrue();
    }

    [Fact]
    public void Cancel_SomeoneElsesVisit_IsRefused()
    {
        var visit = _desk.Book(Alice, Req(Alice)).Visit!;

        _desk.Cancel(Mallory, visit.Id).Should().BeFalse();
        _desk.Get(Alice, visit.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    public void Cancel_AfterStart_IsRefused()
    {
        var visit = _desk.Book(Alice, Req(Alice)).Visit!;
        _time.Advance(TimeSpan.FromDays(1));

        _desk.Cancel(Alice, visit.Id).Should().BeFalse();
        _desk.Get(Alice, visit.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    public void Cancel_Twice_SecondIsFalse()
    {
        var visit = _desk.Book(Alice, Req(Alice)).Visit!;

        _desk.Cancel(Alice, visit.Id).Should().BeTrue();
        _desk.Cancel(Alice, visit.Id).Should().BeFalse();
    }

    [Fact]
    public void Cancel_OldVisitAfterRebook_DoesNotTouchNewBooking()
    {
        var old = _desk.Book(Alice, Req(Alice)).Visit!;
        _desk.Cancel(Alice, old.Id);
        var fresh = _desk.Book(Mallory, Req(Mallory)).Visit!;

        _desk.Cancel(Alice, old.Id).Should().BeFalse();
        _desk.Get(Mallory, fresh.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    public void Cancel_Missing_IsFalse() => _desk.Cancel(Alice, 42).Should().BeFalse();

    [Fact]
    public void ForPatient_GuardianSeesOwnAndDependentsVisits()
    {
        var own = _desk.Book(Alice, Req(Alice)).Visit!;
        var bobs = _desk.Book(Alice, Req(Bob, slot: 2)).Visit!;

        _desk.ForPatient(Alice, Alice).Should().Equal(own);
        _desk.ForPatient(Alice, Bob).Should().Equal(bobs);
        _desk.ForPatient(Bob, Bob).Should().Equal(bobs);
    }

    [Fact]
    public void ForPatient_SomeoneElse_IsEmpty()
    {
        _desk.Book(Alice, Req(Alice));
        _desk.Book(Alice, Req(Bob, slot: 2));

        _desk.ForPatient(Mallory, Alice).Should().BeEmpty();
        _desk.ForPatient(Mallory, Bob).Should().BeEmpty();
        _desk.ForPatient(Bob, Alice).Should().BeEmpty();
    }
}
