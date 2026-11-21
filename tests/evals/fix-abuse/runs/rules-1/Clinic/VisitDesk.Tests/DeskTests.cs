using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

public sealed class DeskTests
{
    private const int Alice = 1;     // guardian of Bobby
    private const int Bobby = 2;     // Alice's dependent
    private const int Mallory = 3;   // unrelated user

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryVisitStore _store = new();
    private readonly Desk _desk;

    public DeskTests()
    {
        _store.AddDependent(Alice, Bobby);
        _store.Add(new Slot(1, ClinicId: 7, StartsAt: _time.GetUtcNow().AddHours(2), Price: 300m));
        _store.Add(new Slot(2, ClinicId: 7, StartsAt: _time.GetUtcNow().AddHours(3), Price: 99.99m));
        _store.Add(new Slot(3, ClinicId: 7, StartsAt: _time.GetUtcNow().AddHours(-1), Price: 300m));
        _store.Add(new Promo("SAVE10", 10));
        _store.Add(new Promo("HALF", 50));
        _desk = new Desk(_store, _time);
    }

    private static BookRequest Req(int patient, int slot, string? promo = null, string? note = null, decimal shown = 0m) =>
        new(patient, slot, shown, promo, note);

    [Fact]
    public void FreeSlots_OneSlotTomorrow_IsListed()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryVisitStore();
        store.Add(new Slot(1, ClinicId: 7, StartsAt: time.GetUtcNow().AddDays(1), Price: 300m));

        var slots = new Desk(store, time).FreeSlots(clinicId: 7);

        slots.Should().ContainSingle(s => s.Id == 1);
    }

    // --- Book: who for ---

    [Fact]
    public void Book_ForSelf_ChargesSlotPrice()
    {
        var result = _desk.Book(Alice, Req(Alice, 1));

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(Alice);
        result.Visit.BookedBy.Should().Be(Alice);
        result.Visit.Charged.Should().Be(300m);
        result.Visit.Status.Should().Be(VisitStatus.Booked);
        result.Visit.Note.Should().BeEmpty();
    }

    [Fact]
    public void Book_ForDependent_IsAllowed_AndRecordsGuardianAsBooker()
    {
        var result = _desk.Book(Alice, Req(Bobby, 1));

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(Bobby);
        result.Visit.BookedBy.Should().Be(Alice);
    }

    [Fact]
    public void Book_ForSomeoneElse_IsRefused()
    {
        _desk.Book(Mallory, Req(Alice, 1)).Refusal.Should().Be(BookRefusal.NotYourPatient);
        _desk.Book(Bobby, Req(Alice, 1)).Refusal.Should().Be(BookRefusal.NotYourPatient);
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Fact]
    public void Book_ShownPrice_IsIgnored()
    {
        var result = _desk.Book(Alice, Req(Alice, 1, shown: 0.01m));

        result.Visit!.Charged.Should().Be(300m);
    }

    // --- Book: slot ---

    [Fact]
    public void Book_UnknownSlot_IsRefused() =>
        _desk.Book(Alice, Req(Alice, 42)).Refusal.Should().Be(BookRefusal.SlotNotFound);

    [Fact]
    public void Book_PastSlot_IsRefused() =>
        _desk.Book(Alice, Req(Alice, 3)).Refusal.Should().Be(BookRefusal.SlotInPast);

    [Fact]
    public void Book_SlotStartingNow_IsRefused()
    {
        _time.Advance(TimeSpan.FromHours(2));

        _desk.Book(Alice, Req(Alice, 1)).Refusal.Should().Be(BookRefusal.SlotInPast);
    }

    [Fact]
    public void Book_TakenSlot_IsRefused()
    {
        _desk.Book(Alice, Req(Alice, 1)).Ok.Should().BeTrue();

        _desk.Book(Mallory, Req(Mallory, 1)).Refusal.Should().Be(BookRefusal.SlotTaken);
        _desk.FreeSlots(7).Select(s => s.Id).Should().Equal(2);
    }

    [Fact]
    public void Book_SameSlotConcurrently_OnlyOneSucceeds()
    {
        var results = Enumerable.Range(100, 200).AsParallel().WithDegreeOfParallelism(16)
            .Select(patient => _desk.Book(patient, Req(patient, 1)))
            .ToList();

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.SlotTaken);
        _store.VisitsForSlot(1).Should().ContainSingle();
    }

    // --- Book: promo ---

    [Fact]
    public void Book_WithPromo_TakesPercentOff()
    {
        var result = _desk.Book(Alice, Req(Alice, 1, promo: "SAVE10"));

        result.Visit!.Charged.Should().Be(270m);
        result.Visit.PromoCode.Should().Be("SAVE10");
    }

    [Fact]
    public void Book_WithPromo_RoundsToCents()
    {
        _desk.Book(Alice, Req(Alice, 2, promo: "HALF")).Visit!.Charged.Should().Be(50.00m); // 49.995 -> 50.00
    }

    [Fact]
    public void Book_PromoCode_IsCaseInsensitiveAndTrimmed_AndStoredInCanonicalForm()
    {
        var result = _desk.Book(Alice, Req(Alice, 1, promo: "  save10 "));

        result.Ok.Should().BeTrue();
        result.Visit!.PromoCode.Should().Be("SAVE10");
    }

    [Fact]
    public void Book_UnknownPromo_IsRefused()
    {
        _desk.Book(Alice, Req(Alice, 1, promo: "FREE100")).Refusal.Should().Be(BookRefusal.UnknownPromo);
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Fact]
    public void Book_BlankPromo_MeansNoPromo()
    {
        var result = _desk.Book(Alice, Req(Alice, 1, promo: "   "));

        result.Ok.Should().BeTrue();
        result.Visit!.PromoCode.Should().BeNull();
        result.Visit.Charged.Should().Be(300m);
    }

    [Fact]
    public void Book_SamePromoTwiceForSamePatient_IsRefused()
    {
        _desk.Book(Alice, Req(Alice, 1, promo: "SAVE10")).Ok.Should().BeTrue();

        _desk.Book(Alice, Req(Alice, 2, promo: "save10")).Refusal.Should().Be(BookRefusal.PromoUsed);
    }

    [Fact]
    public void Book_SamePromo_ForGuardianAndDependent_IsPerPatient()
    {
        _desk.Book(Alice, Req(Alice, 1, promo: "SAVE10")).Ok.Should().BeTrue();

        _desk.Book(Alice, Req(Bobby, 2, promo: "SAVE10")).Ok.Should().BeTrue();
    }

    [Fact]
    public void Book_DifferentPromos_ForSamePatient_AreEachAllowedOnce()
    {
        _desk.Book(Alice, Req(Alice, 1, promo: "SAVE10")).Ok.Should().BeTrue();

        _desk.Book(Alice, Req(Alice, 2, promo: "HALF")).Ok.Should().BeTrue();
    }

    [Fact]
    public void Book_PromoFromCancelledVisit_CanBeUsedAgain()
    {
        var first = _desk.Book(Alice, Req(Alice, 1, promo: "SAVE10")).Visit!;
        _desk.Cancel(Alice, first.Id).Should().BeTrue();

        _desk.Book(Alice, Req(Alice, 2, promo: "SAVE10")).Ok.Should().BeTrue();
    }

    [Fact]
    public void Book_SamePromoConcurrently_ForSamePatient_OnlyOneSucceeds()
    {
        for (var id = 10; id < 60; id++)
            _store.Add(new Slot(id, 7, _time.GetUtcNow().AddDays(1).AddMinutes(id), 100m));

        var results = Enumerable.Range(10, 50).AsParallel().WithDegreeOfParallelism(16)
            .Select(slot => _desk.Book(Alice, Req(Alice, slot, promo: "SAVE10")))
            .ToList();

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.PromoUsed);
    }

    [Fact]
    public void Book_PromoOver100Percent_NeverGoesNegative()
    {
        _store.Add(new Promo("BROKEN", 150));

        _desk.Book(Alice, Req(Alice, 1, promo: "BROKEN")).Visit!.Charged.Should().Be(0m);
    }

    // --- Book: note ---

    [Fact]
    public void Book_NoteOf500Chars_IsKept()
    {
        var note = new string('a', Desk.MaxNoteLength);

        _desk.Book(Alice, Req(Alice, 1, note: note)).Visit!.Note.Should().Be(note);
    }

    [Fact]
    public void Book_NoteOver500Chars_IsRefused()
    {
        _desk.Book(Alice, Req(Alice, 1, note: new string('a', Desk.MaxNoteLength + 1)))
            .Refusal.Should().Be(BookRefusal.NoteTooLong);
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    // --- Get ---

    [Fact]
    public void Get_OwnVisit_AndDependentsVisit_AreVisible()
    {
        var own = _desk.Book(Alice, Req(Alice, 1)).Visit!;
        var bobbys = _desk.Book(Alice, Req(Bobby, 2)).Visit!;

        _desk.Get(Alice, own.Id).Should().Be(own);
        _desk.Get(Alice, bobbys.Id).Should().Be(bobbys);
        _desk.Get(Bobby, bobbys.Id).Should().Be(bobbys);
    }

    [Fact]
    public void Get_SomeoneElsesVisit_IsNull()
    {
        var own = _desk.Book(Alice, Req(Alice, 1)).Visit!;

        _desk.Get(Mallory, own.Id).Should().BeNull();
        _desk.Get(Bobby, own.Id).Should().BeNull(); // a dependent doesn't see the guardian's visits
    }

    [Fact]
    public void Get_UnknownVisit_IsNull() => _desk.Get(Alice, 999).Should().BeNull();

    // --- Cancel ---

    [Fact]
    public void Cancel_BeforeStart_CancelsAndFreesSlot()
    {
        var visit = _desk.Book(Alice, Req(Alice, 1)).Visit!;

        _desk.Cancel(Alice, visit.Id).Should().BeTrue();

        _desk.Get(Alice, visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
        _desk.FreeSlots(7).Select(s => s.Id).Should().Contain(1);
        _desk.Book(Mallory, Req(Mallory, 1)).Ok.Should().BeTrue();
    }

    [Fact]
    public void Cancel_GuardianCancelsDependentsVisit()
    {
        var visit = _desk.Book(Alice, Req(Bobby, 1)).Visit!;

        _desk.Cancel(Alice, visit.Id).Should().BeTrue();
    }

    [Fact]
    public void Cancel_SomeoneElsesVisit_IsRefused()
    {
        var visit = _desk.Book(Alice, Req(Alice, 1)).Visit!;

        _desk.Cancel(Mallory, visit.Id).Should().BeFalse();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    public void Cancel_AfterStart_IsRefused()
    {
        var visit = _desk.Book(Alice, Req(Alice, 1)).Visit!;
        _time.Advance(TimeSpan.FromHours(2));

        _desk.Cancel(Alice, visit.Id).Should().BeFalse();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    public void Cancel_JustBeforeStart_IsAllowed()
    {
        var visit = _desk.Book(Alice, Req(Alice, 1)).Visit!;
        _time.Advance(TimeSpan.FromHours(2) - TimeSpan.FromSeconds(1));

        _desk.Cancel(Alice, visit.Id).Should().BeTrue();
    }

    [Fact]
    public void Cancel_Twice_SecondIsFalse()
    {
        var visit = _desk.Book(Alice, Req(Alice, 1)).Visit!;

        _desk.Cancel(Alice, visit.Id).Should().BeTrue();
        _desk.Cancel(Alice, visit.Id).Should().BeFalse();
    }

    [Fact]
    public void Cancel_UnknownVisit_IsFalse() => _desk.Cancel(Alice, 999).Should().BeFalse();

    // --- ForPatient ---

    [Fact]
    public void ForPatient_GuardianSeesOwnAndDependentsVisits()
    {
        var own = _desk.Book(Alice, Req(Alice, 1)).Visit!;
        var bobbys = _desk.Book(Alice, Req(Bobby, 2)).Visit!;

        _desk.ForPatient(Alice, Alice).Should().Equal(own);
        _desk.ForPatient(Alice, Bobby).Should().Equal(bobbys);
    }

    [Fact]
    public void ForPatient_SomeoneElse_IsEmpty()
    {
        _desk.Book(Alice, Req(Alice, 1));
        _desk.Book(Alice, Req(Bobby, 2));

        _desk.ForPatient(Mallory, Alice).Should().BeEmpty();
        _desk.ForPatient(Mallory, Bobby).Should().BeEmpty();
        _desk.ForPatient(Bobby, Alice).Should().BeEmpty();
    }
}
