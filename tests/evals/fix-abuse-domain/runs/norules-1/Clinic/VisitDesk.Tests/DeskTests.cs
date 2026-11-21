using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

public sealed class DeskTests
{
    private const int Clinic = 7;
    private const int Parent = 100;
    private const int Child = 200;
    private const int Stranger = 300;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryVisitStore _store = new();
    private readonly Desk _desk;

    public DeskTests()
    {
        _desk = new Desk(_store, _time);
        _store.Add(new Slot(1, Clinic, Now.AddDays(1), 300m));
        _store.Add(new Slot(2, Clinic, Now.AddDays(2), 300m));
        _store.Add(new Slot(3, Clinic, Now.AddHours(-1), 300m));
        _store.Add(new Slot(4, ClinicId: 8, Now.AddDays(1), 300m));
        _store.Add(new GuardianLink(Parent, Child, Until: null));
        _store.Add(new Promo("TENOFF", 10, ClinicId: null, ValidUntil: null));
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private BookResult Book(int caller, int patient, int slot = 1, string? promo = null, string? note = null,
        decimal shown = 300m) =>
        _desk.Book(caller, new BookRequest(patient, slot, shown, promo, note));

    [Fact]
    public void FreeSlots_OneSlotTomorrow_IsListed()
    {
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == 1);
    }

    [Fact]
    public void FreeSlots_BookedSlot_IsNotListed_UntilCancelled()
    {
        var visit = Book(Parent, Parent).Visit!;
        _desk.FreeSlots(Clinic).Should().NotContain(s => s.Id == 1);

        _desk.Cancel(Parent, visit.Id).Should().BeTrue();
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == 1);
    }

    // --- Book: who for ---

    [Fact]
    public void Book_ForSelf_ChargesSlotPrice()
    {
        var result = Book(Parent, Parent);

        result.Ok.Should().BeTrue();
        result.Visit.Should().BeEquivalentTo(new
        {
            PatientId = Parent, SlotId = 1, BookedBy = Parent, Charged = 300m, PromoCode = (string?)null,
            Note = "", Status = VisitStatus.Booked,
        });
    }

    [Fact]
    public void Book_ForDependent_IsBookedByGuardian()
    {
        var result = Book(Parent, Child);

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(Child);
        result.Visit.BookedBy.Should().Be(Parent);
    }

    [Fact]
    public void Book_ForSomeoneElse_IsRefused()
    {
        Book(Stranger, Child).Refusal.Should().Be(BookRefusal.NotYourPatient);
        Book(Child, Parent).Refusal.Should().Be(BookRefusal.NotYourPatient);
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == 1);
    }

    [Fact]
    public void Book_ForDependent_AfterLinkEnded_IsRefused()
    {
        _store.Add(new GuardianLink(Stranger, Child, Until: Now.AddMinutes(-1)));

        Book(Stranger, Child).Refusal.Should().Be(BookRefusal.NotYourPatient);
    }

    [Fact]
    public void Book_ForDependent_LinkEndingLater_IsAllowedUntilItEnds()
    {
        _store.Add(new GuardianLink(Stranger, Child, Until: Now.AddHours(1)));
        Book(Stranger, Child, slot: 1).Ok.Should().BeTrue();

        _time.Advance(TimeSpan.FromHours(1));
        Book(Stranger, Child, slot: 2).Refusal.Should().Be(BookRefusal.NotYourPatient);
    }

    // --- Book: slot and price ---

    [Fact]
    public void Book_UnknownSlot_IsRefused() => Book(Parent, Parent, slot: 99).Refusal.Should().Be(BookRefusal.SlotNotFound);

    [Fact]
    public void Book_StartedSlot_IsRefused() => Book(Parent, Parent, slot: 3).Refusal.Should().Be(BookRefusal.SlotInPast);

    [Fact]
    public void Book_TakenSlot_IsRefused()
    {
        Book(Parent, Parent).Ok.Should().BeTrue();

        Book(Stranger, Stranger).Refusal.Should().Be(BookRefusal.SlotTaken);
    }

    [Fact]
    public void Book_ChargesCurrentSlotPrice_NotThePriceTheAppShowed()
    {
        _store.Add(new Slot(1, Clinic, Now.AddDays(1), 450m));

        Book(Parent, Parent, shown: 1m).Visit!.Charged.Should().Be(450m);
    }

    // --- Book: promo ---

    [Fact]
    public void Book_WithPromo_TakesPercentOff()
    {
        var visit = Book(Parent, Parent, promo: " tenoff ").Visit!;

        visit.Charged.Should().Be(270m);
        visit.PromoCode.Should().Be("TENOFF");
    }

    [Fact]
    public void Book_UnknownPromo_IsRefused_AndSlotStaysFree()
    {
        Book(Parent, Parent, promo: "NOPE").Refusal.Should().Be(BookRefusal.UnknownPromo);
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == 1);
    }

    [Fact]
    public void Book_BlankPromo_IsNoPromo() => Book(Parent, Parent, promo: "  ").Visit!.Charged.Should().Be(300m);

    [Fact]
    public void Book_ExpiredPromo_IsRefused()
    {
        _store.Add(new Promo("OLD", 50, null, ValidUntil: Now));

        Book(Parent, Parent, promo: "OLD").Refusal.Should().Be(BookRefusal.UnknownPromo);
    }

    [Fact]
    public void Book_PromoForAnotherClinic_IsRefused()
    {
        _store.Add(new Promo("EIGHT", 50, ClinicId: 8, null));

        Book(Parent, Parent, slot: 1, promo: "EIGHT").Refusal.Should().Be(BookRefusal.UnknownPromo);
        Book(Parent, Parent, slot: 4, promo: "EIGHT").Visit!.Charged.Should().Be(150m);
    }

    [Fact]
    public void Book_SamePromoTwice_ForSamePatient_IsRefused()
    {
        Book(Parent, Parent, slot: 1, promo: "TENOFF").Ok.Should().BeTrue();

        Book(Parent, Parent, slot: 2, promo: "tenoff").Refusal.Should().Be(BookRefusal.PromoUsed);
    }

    [Fact]
    public void Book_PromoIsPerPatient_GuardianAndDependentEachGetIt()
    {
        Book(Parent, Parent, slot: 1, promo: "TENOFF").Ok.Should().BeTrue();

        Book(Parent, Child, slot: 2, promo: "TENOFF").Ok.Should().BeTrue();
        Book(Parent, Child, slot: 4, promo: "TENOFF").Refusal.Should().Be(BookRefusal.PromoUsed);
    }

    [Fact]
    public void Book_PromoOnCancelledVisit_CanBeUsedAgain()
    {
        var first = Book(Parent, Parent, slot: 1, promo: "TENOFF").Visit!;
        _desk.Cancel(Parent, first.Id).Should().BeTrue();

        Book(Parent, Parent, slot: 2, promo: "TENOFF").Ok.Should().BeTrue();
    }

    [Theory]
    [InlineData(150, 0)]
    [InlineData(-20, 300)]
    public void Book_PromoPercentOutOfRange_IsClamped(int percent, decimal charged)
    {
        _store.Add(new Promo("ODD", percent, null, null));

        Book(Parent, Parent, promo: "ODD").Visit!.Charged.Should().Be(charged);
    }

    [Fact]
    public void Book_Discount_IsRoundedToCents()
    {
        _store.Add(new Slot(5, Clinic, Now.AddDays(1), 99.99m));
        _store.Add(new Promo("THIRD", 33, null, null));

        Book(Parent, Parent, slot: 5, promo: "THIRD").Visit!.Charged.Should().Be(66.99m);
    }

    // --- Book: note ---

    [Fact]
    public void Book_NoteUpTo500_IsKept()
    {
        var note = new string('a', Desk.MaxNoteLength);

        Book(Parent, Parent, note: note).Visit!.Note.Should().Be(note);
    }

    [Fact]
    public void Book_NoteOver500_IsRefused() =>
        Book(Parent, Parent, note: new string('a', Desk.MaxNoteLength + 1)).Refusal.Should().Be(BookRefusal.NoteTooLong);

    // --- Get / ForPatient ---

    [Fact]
    public void Get_PatientAndGuardianSeeVisit_OthersDont()
    {
        var visit = Book(Parent, Child).Visit!;

        _desk.Get(Child, visit.Id).Should().Be(visit);
        _desk.Get(Parent, visit.Id).Should().Be(visit);
        _desk.Get(Stranger, visit.Id).Should().BeNull();
        _desk.Get(Parent, 999).Should().BeNull();
    }

    [Fact]
    public void Get_GuardianWhoseLinkEnded_NoLongerSeesVisitTheyBooked()
    {
        _store.Add(new GuardianLink(Stranger, Child, Until: Now.AddHours(1)));
        var visit = Book(Stranger, Child).Visit!;

        _time.Advance(TimeSpan.FromHours(2));

        _desk.Get(Stranger, visit.Id).Should().BeNull();
        _desk.ForPatient(Stranger, Child).Should().BeEmpty();
        _desk.Cancel(Stranger, visit.Id).Should().BeFalse();
    }

    [Fact]
    public void ForPatient_GuardianSeesOwnAndDependentsVisits()
    {
        var own = Book(Parent, Parent, slot: 1).Visit!;
        var childs = Book(Parent, Child, slot: 2).Visit!;

        _desk.ForPatient(Parent, Parent).Should().Equal(own);
        _desk.ForPatient(Parent, Child).Should().Equal(childs);
    }

    [Fact]
    public void ForPatient_SomeoneElse_IsEmpty()
    {
        Book(Parent, Parent, slot: 1);
        Book(Parent, Child, slot: 2);

        _desk.ForPatient(Stranger, Parent).Should().BeEmpty();
        _desk.ForPatient(Child, Parent).Should().BeEmpty();
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_RefundsWhatWasCharged_NotTheCurrentPrice()
    {
        var visit = Book(Parent, Parent, promo: "TENOFF").Visit!;
        _store.Add(new Slot(1, Clinic, Now.AddDays(1), 999m));

        _desk.Cancel(Parent, visit.Id).Should().BeTrue();

        _store.Refunds.Should().Equal((visit.Id, 270m));
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
    }

    [Fact]
    public void Cancel_ByGuardianOrDependent_IsAllowed()
    {
        var a = Book(Parent, Child, slot: 1).Visit!;
        var b = Book(Parent, Child, slot: 2).Visit!;

        _desk.Cancel(Parent, a.Id).Should().BeTrue();
        _desk.Cancel(Child, b.Id).Should().BeTrue();
    }

    [Fact]
    public void Cancel_BySomeoneElse_IsRefused_AndNothingRefunded()
    {
        var visit = Book(Parent, Parent).Visit!;

        _desk.Cancel(Stranger, visit.Id).Should().BeFalse();
        _desk.Cancel(Child, visit.Id).Should().BeFalse();

        _store.Refunds.Should().BeEmpty();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    public void Cancel_Twice_RefundsOnce()
    {
        var visit = Book(Parent, Parent).Visit!;

        _desk.Cancel(Parent, visit.Id).Should().BeTrue();
        _desk.Cancel(Parent, visit.Id).Should().BeFalse();

        _store.Refunds.Should().HaveCount(1);
    }

    [Fact]
    public void Cancel_UntilItStarts()
    {
        var a = Book(Parent, Parent, slot: 1).Visit!;
        var b = Book(Parent, Parent, slot: 2).Visit!;

        _time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromTicks(1));
        _desk.Cancel(Parent, a.Id).Should().BeTrue();

        _time.Advance(TimeSpan.FromDays(1) + TimeSpan.FromTicks(1));
        _desk.Cancel(Parent, b.Id).Should().BeFalse();
        _store.Refunds.Should().ContainSingle();
    }

    [Fact]
    public void Cancel_UnknownVisit_IsFalse() => _desk.Cancel(Parent, 999).Should().BeFalse();

    // --- Many callers at once ---

    [Fact]
    public async Task Book_ManyCallersSameSlot_OnlyOneGetsIt()
    {
        const int callers = 64;
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(1000, callers)
            .Select(id => Task.Run(() => { start.Wait(); return Book(id, id); }))
            .ToArray();
        start.Set();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.SlotTaken);
        _store.VisitsForSlot(1).Should().ContainSingle();
    }

    [Fact]
    public async Task Book_SamePromoConcurrently_UsedOnce()
    {
        for (var id = 10; id < 40; id++)
            _store.Add(new Slot(id, Clinic, Now.AddDays(3).AddHours(id), 100m));
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(10, 30)
            .Select(slot => Task.Run(() => { start.Wait(); return Book(Parent, Parent, slot, promo: "TENOFF"); }))
            .ToArray();
        start.Set();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.PromoUsed);
    }

    [Fact]
    public async Task Cancel_Concurrently_RefundsOnce()
    {
        var visit = Book(Parent, Parent).Visit!;
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => { start.Wait(); return _desk.Cancel(Parent, visit.Id); }))
            .ToArray();
        start.Set();

        var results = await Task.WhenAll(tasks);

        results.Count(r => r).Should().Be(1);
        _store.Refunds.Should().ContainSingle();
    }
}
