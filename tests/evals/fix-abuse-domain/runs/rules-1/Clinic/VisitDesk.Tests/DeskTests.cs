using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

public sealed class DeskTests
{
    private const int Clinic = 7;
    private const int Guardian = 100;
    private const int Child = 200;
    private const int Stranger = 300;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryVisitStore _store = new();
    private readonly Desk _desk;

    public DeskTests()
    {
        _desk = new Desk(_store, _time);
        _store.Add(new Slot(1, Clinic, Now.AddHours(2), 300m));
        _store.Add(new Slot(2, Clinic, Now.AddHours(3), 300m));
        _store.Add(new Slot(3, Clinic, Now.AddHours(4), 99.99m));
        _store.Add(new Slot(9, Clinic, Now.AddHours(-1), 300m));
        _store.Add(new GuardianLink(Guardian, Child, Until: null));
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    private static BookRequest Req(int patient, int slot, string? promo = null, string? note = null, decimal shown = 300m) =>
        new(patient, slot, shown, promo, note);

    // ---- free slots ----

    [Fact]
    public void FreeSlots_OneSlotTomorrow_IsListed()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        var store = new InMemoryVisitStore();
        store.Add(new Slot(1, ClinicId: 7, StartsAt: time.GetUtcNow().AddDays(1), Price: 300m));

        var slots = new Desk(store, time).FreeSlots(clinicId: 7);

        slots.Should().ContainSingle(s => s.Id == 1);
    }

    [Fact]
    public void FreeSlots_BookedSlotIsHidden_AndReturnsAfterCancel()
    {
        var visit = _desk.Book(Guardian, Req(Guardian, 1)).Visit!;
        _desk.FreeSlots(Clinic).Should().NotContain(s => s.Id == 1);

        _desk.Cancel(Guardian, visit.Id).Should().BeTrue();
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == 1);
    }

    // ---- booking: who for ----

    [Fact]
    public void Book_ForSelf_ChargesSlotPrice()
    {
        var result = _desk.Book(Guardian, Req(Guardian, 1));

        result.Ok.Should().BeTrue();
        result.Visit!.Should().BeEquivalentTo(new { PatientId = Guardian, SlotId = 1, BookedBy = Guardian, Charged = 300m, Note = "", Status = VisitStatus.Booked });
    }

    [Fact]
    public void Book_ForDependent_IsAllowedAndRecordsWhoBooked()
    {
        var result = _desk.Book(Guardian, Req(Child, 1));

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(Child);
        result.Visit.BookedBy.Should().Be(Guardian);
    }

    [Fact]
    public void Book_ForSomeoneElse_IsRefused()
    {
        _desk.Book(Stranger, Req(Child, 1)).Refusal.Should().Be(BookRefusal.NotYourPatient);
        _desk.Book(Child, Req(Guardian, 1)).Refusal.Should().Be(BookRefusal.NotYourPatient, "the link only goes guardian -> dependent");
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Fact]
    public void Book_ForDependentWhoseLinkHasEnded_IsRefused()
    {
        _store.Add(new GuardianLink(Guardian, 201, Until: Now.AddDays(-1)));

        _desk.Book(Guardian, Req(201, 1)).Refusal.Should().Be(BookRefusal.NotYourPatient);
    }

    [Fact]
    public void Book_ForDependentWhoseLinkEndsLater_IsAllowed()
    {
        _store.Add(new GuardianLink(Guardian, 202, Until: Now.AddDays(30)));

        _desk.Book(Guardian, Req(202, 1)).Ok.Should().BeTrue();
    }

    // ---- booking: slot ----

    [Fact]
    public void Book_UnknownSlot_IsRefused() =>
        _desk.Book(Guardian, Req(Guardian, 42)).Refusal.Should().Be(BookRefusal.SlotNotFound);

    [Fact]
    public void Book_StartedSlot_IsRefused()
    {
        _desk.Book(Guardian, Req(Guardian, 9)).Refusal.Should().Be(BookRefusal.SlotInPast);

        _time.Advance(TimeSpan.FromHours(2));
        _desk.Book(Guardian, Req(Guardian, 1)).Refusal.Should().Be(BookRefusal.SlotInPast, "a slot starting right now has started");
    }

    [Fact]
    public void Book_TakenSlot_IsRefused()
    {
        _desk.Book(Guardian, Req(Guardian, 1)).Ok.Should().BeTrue();

        _desk.Book(Stranger, Req(Stranger, 1)).Refusal.Should().Be(BookRefusal.SlotTaken);
    }

    [Fact]
    public void Book_UsesTodaysPrice_NotThePriceTheAppShowed()
    {
        _store.Add(new Slot(1, Clinic, Now.AddHours(2), 350m));

        var result = _desk.Book(Guardian, Req(Guardian, 1, shown: 1m));

        result.Visit!.Charged.Should().Be(350m);
    }

    [Fact]
    public void Book_SameSlotFromManyCallersAtOnce_BooksItOnce()
    {
        var callers = Enumerable.Range(1000, 64).ToArray();

        var results = callers.AsParallel().WithDegreeOfParallelism(16)
            .Select(c => new Desk(_store, _time).Book(c, Req(c, 1))) // a desk per request, as a scoped API would
            .ToList();

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.SlotTaken);
        _store.VisitsForSlot(1).Should().ContainSingle();
    }

    // ---- booking: note ----

    [Fact]
    public void Book_NoteUpToLimit_IsKept()
    {
        var note = new string('a', Desk.MaxNoteLength);

        _desk.Book(Guardian, Req(Guardian, 1, note: note)).Visit!.Note.Should().Be(note);
    }

    [Fact]
    public void Book_NoteOverLimit_IsRefused()
    {
        _desk.Book(Guardian, Req(Guardian, 1, note: new string('a', Desk.MaxNoteLength + 1)))
            .Refusal.Should().Be(BookRefusal.NoteTooLong);
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    // ---- booking: promo ----

    [Fact]
    public void Book_WithPromo_TakesPercentOff()
    {
        _store.Add(new Promo("WINTER20", 20, null, null));

        var visit = _desk.Book(Guardian, Req(Guardian, 1, promo: " winter20 ")).Visit!;

        visit.Charged.Should().Be(240m);
        visit.PromoCode.Should().Be("WINTER20");
    }

    [Fact]
    public void Book_WithPromo_RoundsToCents()
    {
        _store.Add(new Promo("THIRD", 33, null, null));

        _desk.Book(Guardian, Req(Guardian, 3, promo: "THIRD")).Visit!.Charged.Should().Be(66.99m); // 99.99 * 0.67 = 66.9933
    }

    [Fact]
    public void Book_BlankPromo_IsNoPromo()
    {
        var visit = _desk.Book(Guardian, Req(Guardian, 1, promo: "  ")).Visit!;

        visit.Charged.Should().Be(300m);
        visit.PromoCode.Should().BeNull();
    }

    [Fact]
    public void Book_UnknownPromo_IsRefused()
    {
        _desk.Book(Guardian, Req(Guardian, 1, promo: "NOPE")).Refusal.Should().Be(BookRefusal.UnknownPromo);
        _store.VisitsForSlot(1).Should().BeEmpty();
    }

    [Fact]
    public void Book_PromoForAnotherClinic_IsRefused()
    {
        _store.Add(new Promo("OTHER", 50, ClinicId: 8, null));

        _desk.Book(Guardian, Req(Guardian, 1, promo: "OTHER")).Refusal.Should().Be(BookRefusal.UnknownPromo);
    }

    [Fact]
    public void Book_PromoForThisClinic_Works()
    {
        _store.Add(new Promo("HERE", 50, ClinicId: Clinic, null));

        _desk.Book(Guardian, Req(Guardian, 1, promo: "HERE")).Visit!.Charged.Should().Be(150m);
    }

    [Fact]
    public void Book_ExpiredPromo_IsRefused()
    {
        _store.Add(new Promo("OLD", 50, null, ValidUntil: Now.AddMinutes(-1)));

        _desk.Book(Guardian, Req(Guardian, 1, promo: "OLD")).Refusal.Should().Be(BookRefusal.UnknownPromo);
    }

    [Fact]
    public void Book_PromoTwiceForSamePatient_IsRefused()
    {
        _store.Add(new Promo("ONCE", 10, null, null));
        _desk.Book(Guardian, Req(Guardian, 1, promo: "ONCE")).Ok.Should().BeTrue();

        _desk.Book(Guardian, Req(Guardian, 2, promo: "once")).Refusal.Should().Be(BookRefusal.PromoUsed);
    }

    [Fact]
    public void Book_PromoIsPerPatient_SoGuardianAndDependentEachGetIt()
    {
        _store.Add(new Promo("ONCE", 10, null, null));

        _desk.Book(Guardian, Req(Guardian, 1, promo: "ONCE")).Ok.Should().BeTrue();
        _desk.Book(Guardian, Req(Child, 2, promo: "ONCE")).Ok.Should().BeTrue();
    }

    [Fact]
    public void Book_PromoFromCancelledVisit_CanBeUsedAgain()
    {
        _store.Add(new Promo("ONCE", 10, null, null));
        var first = _desk.Book(Guardian, Req(Guardian, 1, promo: "ONCE")).Visit!;
        _desk.Cancel(Guardian, first.Id);

        _desk.Book(Guardian, Req(Guardian, 2, promo: "ONCE")).Ok.Should().BeTrue();
    }

    [Fact]
    public void Book_SamePromoSamePatientAtOnce_UsesItOnce()
    {
        _store.Add(new Promo("ONCE", 10, null, null));
        var slots = Enumerable.Range(500, 32).ToArray();
        foreach (var s in slots)
            _store.Add(new Slot(s, Clinic, Now.AddDays(1).AddMinutes(s), 300m));

        var results = slots.AsParallel().WithDegreeOfParallelism(16)
            .Select(s => new Desk(_store, _time).Book(Guardian, Req(Guardian, s, promo: "ONCE")))
            .ToList();

        results.Count(r => r.Ok).Should().Be(1);
    }

    // ---- get / for patient ----

    [Fact]
    public void Get_PatientAndGuardianSeeIt_StrangerDoesNot()
    {
        var visit = _desk.Book(Guardian, Req(Child, 1)).Visit!;

        _desk.Get(Child, visit.Id).Should().Be(visit);
        _desk.Get(Guardian, visit.Id).Should().Be(visit);
        _desk.Get(Stranger, visit.Id).Should().BeNull();
        _desk.Get(Guardian, 999).Should().BeNull();
    }

    [Fact]
    public void Get_DependentCannotSeeGuardiansVisit()
    {
        var visit = _desk.Book(Guardian, Req(Guardian, 1)).Visit!;

        _desk.Get(Child, visit.Id).Should().BeNull();
    }

    [Fact]
    public void Get_AfterLinkEnds_FormerGuardianNoLongerSeesIt()
    {
        _store.Add(new GuardianLink(Guardian, 203, Until: Now.AddDays(1)));
        var visit = _desk.Book(Guardian, Req(203, 1)).Visit!;

        _time.Advance(TimeSpan.FromDays(2));

        _desk.Get(Guardian, visit.Id).Should().BeNull();
        _desk.ForPatient(Guardian, 203).Should().BeEmpty();
        _desk.Get(203, visit.Id).Should().NotBeNull();
    }

    [Fact]
    public void ForPatient_GuardianSeesDependentsVisits_StrangerSeesNone()
    {
        var a = _desk.Book(Guardian, Req(Child, 1)).Visit!;
        var b = _desk.Book(Child, Req(Child, 2)).Visit!;
        _desk.Book(Guardian, Req(Guardian, 3));

        _desk.ForPatient(Guardian, Child).Should().BeEquivalentTo([a, b]);
        _desk.ForPatient(Child, Child).Should().BeEquivalentTo([a, b]);
        _desk.ForPatient(Stranger, Child).Should().BeEmpty();
        _desk.ForPatient(Child, Guardian).Should().BeEmpty();
    }

    // ---- cancel ----

    [Fact]
    public void Cancel_BeforeStart_CancelsAndRefundsWhatWasCharged()
    {
        _store.Add(new Promo("HALF", 50, null, null));
        var visit = _desk.Book(Guardian, Req(Guardian, 1, promo: "HALF")).Visit!;
        _store.Add(new Slot(1, Clinic, Now.AddHours(2), 999m)); // price changed since booking

        _desk.Cancel(Guardian, visit.Id).Should().BeTrue();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
        _store.Refunds.Should().Equal((visit.Id, 150m));
    }

    [Fact]
    public void Cancel_ByGuardianForDependent_Works()
    {
        var visit = _desk.Book(Child, Req(Child, 1)).Visit!;

        _desk.Cancel(Guardian, visit.Id).Should().BeTrue();
    }

    [Fact]
    public void Cancel_ByStranger_IsRefusedAndRefundsNothing()
    {
        var visit = _desk.Book(Guardian, Req(Guardian, 1)).Visit!;

        _desk.Cancel(Stranger, visit.Id).Should().BeFalse();
        _desk.Cancel(Child, visit.Id).Should().BeFalse();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
        _store.Refunds.Should().BeEmpty();
    }

    [Fact]
    public void Cancel_AfterStart_IsRefused()
    {
        var visit = _desk.Book(Guardian, Req(Guardian, 1)).Visit!;
        _time.Advance(TimeSpan.FromHours(2));

        _desk.Cancel(Guardian, visit.Id).Should().BeFalse();
        _store.Refunds.Should().BeEmpty();
    }

    [Fact]
    public void Cancel_Twice_RefundsOnce()
    {
        var visit = _desk.Book(Guardian, Req(Guardian, 1)).Visit!;

        _desk.Cancel(Guardian, visit.Id).Should().BeTrue();
        _desk.Cancel(Guardian, visit.Id).Should().BeFalse();

        _store.Refunds.Should().ContainSingle();
    }

    [Fact]
    public void Cancel_ManyTimesAtOnce_RefundsOnce()
    {
        var visit = _desk.Book(Guardian, Req(Guardian, 1)).Visit!;

        var results = Enumerable.Range(0, 32).AsParallel().WithDegreeOfParallelism(16)
            .Select(_ => new Desk(_store, _time).Cancel(Guardian, visit.Id))
            .ToList();

        results.Count(ok => ok).Should().Be(1);
        _store.Refunds.Should().ContainSingle();
    }

    [Fact]
    public void Cancel_UnknownVisit_IsFalse() => _desk.Cancel(Guardian, 999).Should().BeFalse();
}
