using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

/// <summary>
/// The booking story's criteria (story.md at the work dir root): book, charge, promo, note, cancel, see.
/// Written before Book, Get, Cancel and ForPatient exist; the clock is fixed.
/// </summary>
public sealed class BookingStoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private const int ClinicId = 7;

    // People. Guardian 100 has dependent 101; OtherGuardian 300 has dependent 301; 200 and 400 are on their own.
    private const int Guardian = 100;
    private const int Dependent = 101;
    private const int Stranger = 200;
    private const int OtherGuardian = 300;
    private const int OtherDependent = 301;
    private const int Alone = 400;

    // Slots, all in the future unless a test adds its own.
    private const int Tomorrow = 1;      // 300.00
    private const int DayAfter = 2;      // 300.00
    private const int Expensive = 3;     // 450.00
    private const int UnknownSlot = 999;

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryVisitStore _store = new();
    private readonly Desk _desk;

    public BookingStoryTests()
    {
        _store.AddDependent(Guardian, Dependent);
        _store.AddDependent(OtherGuardian, OtherDependent);
        _store.Add(new Slot(Tomorrow, ClinicId, Now.AddDays(1), 300m));
        _store.Add(new Slot(DayAfter, ClinicId, Now.AddDays(2), 300m));
        _store.Add(new Slot(Expensive, ClinicId, Now.AddDays(3), 450m));
        _store.Add(new Promo("SAVE25", 25));
        _store.Add(new Promo("ZERO", 0));
        _store.Add(new Promo("FREE", 100));
        _desk = new Desk(_store, _time);
    }

    private BookRequest Req(int patientId, int slotId, string? promo = null, string? note = null, decimal? shownPrice = null) =>
        new(patientId, slotId, shownPrice ?? _store.FindSlot(slotId)?.Price ?? 0m, promo, note);

    private Visit BookOk(int callerId, BookRequest request)
    {
        var result = _desk.Book(callerId, request);
        result.Refusal.Should().Be(BookRefusal.None);
        result.Visit.Should().NotBeNull();
        return result.Visit!;
    }

    // ---------------- AC1, AC2: who books for whom ----------------

    [Fact]
    [Trait("AC", "AC1"), Trait("AC", "AC3"), Trait("AC", "AC4")]
    public void Book_PatientBooksFreeSlotForThemselves_VisitIsBookedForCallerAtSlotPrice()
    {
        var result = _desk.Book(Stranger, Req(Stranger, Tomorrow));

        result.Ok.Should().BeTrue();
        result.Refusal.Should().Be(BookRefusal.None);
        result.Visit.Should().BeEquivalentTo(new
        {
            PatientId = Stranger,
            SlotId = Tomorrow,
            BookedBy = Stranger,
            Charged = 300m,
            PromoCode = (string?)null,
            Status = VisitStatus.Booked,
        });
        _store.VisitsForSlot(Tomorrow).Should().ContainSingle(v => v.PatientId == Stranger);
    }

    [Fact]
    [Trait("AC", "AC2")]
    public void Book_GuardianBooksForDependent_VisitIsForDependentBookedByGuardian()
    {
        var visit = BookOk(Guardian, Req(Dependent, Tomorrow));

        visit.PatientId.Should().Be(Dependent);
        visit.BookedBy.Should().Be(Guardian);
        visit.Status.Should().Be(VisitStatus.Booked);
        _desk.Mine(Dependent).Should().ContainSingle(v => v.Id == visit.Id);
        _desk.Mine(Guardian).Should().BeEmpty();
    }

    // Decision table: the caller, the patient asked for, and the guardianship between them.
    [Theory]
    [InlineData(Guardian, Guardian, BookRefusal.None)]              // self
    [InlineData(Dependent, Dependent, BookRefusal.None)]            // a dependent is a patient too (Q9)
    [InlineData(Guardian, Dependent, BookRefusal.None)]             // guardian -> own dependent
    [InlineData(OtherGuardian, Dependent, BookRefusal.NotYourPatient)] // another family's dependent (EC1)
    [InlineData(Guardian, OtherDependent, BookRefusal.NotYourPatient)] // another family's dependent (EC1)
    [InlineData(Dependent, Guardian, BookRefusal.NotYourPatient)]   // one way only (EC2)
    [InlineData(Stranger, Alone, BookRefusal.NotYourPatient)]       // no guardianship at all (EC3)
    [InlineData(Stranger, Dependent, BookRefusal.NotYourPatient)]   // someone else's dependent
    [Trait("AC", "AC1"), Trait("AC", "AC2"), Trait("EC", "EC1"), Trait("EC", "EC2"), Trait("EC", "EC3")]
    public void Book_WhoBooksForWhom_FollowsTheDecisionTable(int callerId, int patientId, BookRefusal expected)
    {
        var result = _desk.Book(callerId, Req(patientId, Tomorrow));

        result.Refusal.Should().Be(expected);
        if (expected == BookRefusal.None)
            result.Visit!.Should().Match<Visit>(v => v.PatientId == patientId && v.BookedBy == callerId);
        else
        {
            result.Visit.Should().BeNull();
            _store.VisitsForSlot(Tomorrow).Should().BeEmpty();
        }
    }

    [Fact]
    [Trait("AC", "AC2"), Trait("EC", "EC1")]
    public void Book_GuardianBooksForAnotherFamilysDependent_IsRefusedAndBooksNothing()
    {
        // Given a guardian of patient 301 and another guardian, 100, who is not
        // When 100 books tomorrow's slot for 301
        var result = _desk.Book(Guardian, Req(OtherDependent, Tomorrow));

        // Then the booking is refused as not their patient, 301 has no visit, and the slot is still free
        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        result.Visit.Should().BeNull();
        _store.VisitsOf(OtherDependent).Should().BeEmpty();
        _desk.FreeSlots(ClinicId).Should().Contain(s => s.Id == Tomorrow);
    }

    // ---------------- AC3, AC4: what the visit costs ----------------

    [Theory]
    [InlineData(Tomorrow, 300)]
    [InlineData(Expensive, 450)]
    [Trait("AC", "AC3")]
    public void Book_NoPromo_ChargesTheSlotsPrice(int slotId, int price)
    {
        var visit = BookOk(Stranger, Req(Stranger, slotId));

        visit.Charged.Should().Be(price);
        visit.PromoCode.Should().BeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(299.99)]
    [InlineData(1000)]
    [Trait("AC", "AC3"), Trait("EC", "EC4")]
    public void Book_ShownPriceDiffersFromSlotPrice_ChargesTheSlotsPrice(decimal shownPrice)
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, shownPrice: shownPrice));

        visit.Charged.Should().Be(300m);
    }

    [Fact]
    [Trait("AC", "AC4")]
    public void Book_KnownPromo_TakesItsPercentOffAndKeepsTheCode()
    {
        // Given a 450.00 slot and the clinic's code SAVE25 for 25 percent off
        // When a patient books it with SAVE25
        var visit = BookOk(Stranger, Req(Stranger, Expensive, promo: "SAVE25"));

        // Then they are charged 337.50, and the visit records the code
        visit.Charged.Should().Be(337.50m);
        visit.PromoCode.Should().Be("SAVE25");
    }

    [Fact]
    [Trait("AC", "AC4"), Trait("EC", "EC5")]
    public void Book_ZeroPercentPromo_ChargesFullPrice()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, promo: "ZERO"));

        visit.Charged.Should().Be(300m);
    }

    [Fact]
    [Trait("AC", "AC4"), Trait("EC", "EC6")]
    public void Book_HundredPercentPromo_ChargesZero()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, promo: "FREE"));

        visit.Charged.Should().Be(0m);
    }

    [Fact]
    [Trait("AC", "AC4"), Trait("EC", "EC7")]
    public void Book_PromoTypedInOtherCase_TakesItsPercentOff()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, promo: "save25"));

        visit.Charged.Should().Be(225m);
    }

    // ---------------- AC5: unknown code ----------------

    [Fact]
    [Trait("AC", "AC5")]
    public void Book_UnknownPromo_IsRefusedWithUnknownPromo()
    {
        var result = _desk.Book(Stranger, Req(Stranger, Tomorrow, promo: "SAVE99"));

        result.Refusal.Should().Be(BookRefusal.UnknownPromo);
        result.Ok.Should().BeFalse();
        result.Visit.Should().BeNull();
    }

    [Fact]
    [Trait("AC", "AC5"), Trait("EC", "EC8")]
    public void Book_UnknownPromo_LeavesSlotFreeAndPatientWithoutVisit()
    {
        _desk.Book(Stranger, Req(Stranger, Tomorrow, promo: "SAVE99"));

        _store.VisitsOf(Stranger).Should().BeEmpty();
        _store.VisitsForSlot(Tomorrow).Should().BeEmpty();
        _desk.FreeSlots(ClinicId).Should().Contain(s => s.Id == Tomorrow);
    }

    // ---------------- AC6: once per patient ----------------

    [Fact]
    [Trait("AC", "AC6")]
    public void Book_SamePatientUsesPromoTwice_SecondIsRefusedWithPromoUsed()
    {
        // Given a patient who booked tomorrow with SAVE25
        BookOk(Stranger, Req(Stranger, Tomorrow, promo: "SAVE25"));

        // When they book another slot with SAVE25
        var second = _desk.Book(Stranger, Req(Stranger, DayAfter, promo: "SAVE25"));

        // Then it is refused as used, and they still have only the first visit
        second.Refusal.Should().Be(BookRefusal.PromoUsed);
        second.Visit.Should().BeNull();
        _store.VisitsOf(Stranger).Should().ContainSingle(v => v.SlotId == Tomorrow);
        _desk.FreeSlots(ClinicId).Should().Contain(s => s.Id == DayAfter);
    }

    [Fact]
    [Trait("AC", "AC6"), Trait("EC", "EC9")]
    public void Book_SamePatientReusesPromoInOtherCase_IsRefusedWithPromoUsed()
    {
        BookOk(Stranger, Req(Stranger, Tomorrow, promo: "SAVE25"));

        var second = _desk.Book(Stranger, Req(Stranger, DayAfter, promo: "save25"));

        second.Refusal.Should().Be(BookRefusal.PromoUsed);
    }

    [Fact]
    [Trait("AC", "AC6"), Trait("EC", "EC10")]
    public void Book_PromoUsedByAnotherPatient_StillTakesItsPercentOff()
    {
        BookOk(Stranger, Req(Stranger, Tomorrow, promo: "SAVE25"));

        var visit = BookOk(Alone, Req(Alone, DayAfter, promo: "SAVE25"));

        visit.Charged.Should().Be(225m);
        visit.PromoCode.Should().Be("SAVE25");
    }

    [Fact]
    [Trait("AC", "AC6")]
    public void Book_SamePatientSecondVisitWithoutPromo_IsChargedFullPrice()
    {
        BookOk(Stranger, Req(Stranger, Tomorrow, promo: "SAVE25"));

        var visit = BookOk(Stranger, Req(Stranger, DayAfter));

        visit.Charged.Should().Be(300m);
        visit.PromoCode.Should().BeNull();
    }

    // ---------------- AC7: the note ----------------

    [Fact]
    [Trait("AC", "AC7")]
    public void Book_ShortNote_IsKeptAsWritten()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, note: "Allergic to penicillin."));

        visit.Note.Should().Be("Allergic to penicillin.");
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC11")]
    public void Book_NoteOfExactly500Characters_IsKept()
    {
        var note = new string('a', Desk.MaxNoteLength);
        note.Length.Should().Be(500);

        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, note: note));

        visit.Note.Should().Be(note);
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC12")]
    public void Book_NoteOf501Characters_IsRefusedWithNoteTooLong()
    {
        var result = _desk.Book(Stranger, Req(Stranger, Tomorrow, note: new string('a', 501)));

        result.Refusal.Should().Be(BookRefusal.NoteTooLong);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(Tomorrow).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC13")]
    public void Book_NullNote_IsStoredAsEmptyString()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, note: null));

        visit.Note.Should().NotBeNull().And.BeEmpty();
        _store.FindVisit(visit.Id)!.Note.Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC7"), Trait("EC", "EC14")]
    public void Book_ArabicNoteOf500Characters_IsKept()
    {
        var note = new string('ع', 500); // Arabic letter ain, one UTF-16 char each

        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, note: note));

        visit.Note.Should().Be(note);
    }

    // ---------------- AC8: only a free slot ----------------

    [Fact]
    [Trait("AC", "AC8"), Trait("EC", "EC19")]
    public void Book_UnknownSlot_IsRefusedWithSlotNotFound()
    {
        var result = _desk.Book(Stranger, new BookRequest(Stranger, UnknownSlot, 300m, null, null));

        result.Refusal.Should().Be(BookRefusal.SlotNotFound);
        result.Visit.Should().BeNull();
        _store.VisitsOf(Stranger).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC8"), Trait("EC", "EC18")]
    public void Book_SlotAlreadyBooked_IsRefusedWithSlotTakenAndNoSecondVisit()
    {
        var first = BookOk(Stranger, Req(Stranger, Tomorrow));

        var second = _desk.Book(Alone, Req(Alone, Tomorrow));

        second.Refusal.Should().Be(BookRefusal.SlotTaken);
        second.Visit.Should().BeNull();
        _store.VisitsForSlot(Tomorrow).Should().ContainSingle(v => v.Id == first.Id);
    }

    [Fact]
    [Trait("AC", "AC8")]
    public void Book_SamePatientBooksSameSlotTwice_SecondIsRefusedWithSlotTaken()
    {
        BookOk(Stranger, Req(Stranger, Tomorrow));

        var second = _desk.Book(Stranger, Req(Stranger, Tomorrow));

        second.Refusal.Should().Be(BookRefusal.SlotTaken);
        _store.VisitsForSlot(Tomorrow).Should().ContainSingle();
    }

    // Boundary on StartsAt against now, one tick either side (FreeSlots counts a slot free while StartsAt > now).
    [Theory]
    [InlineData(-1, BookRefusal.SlotInPast)]   // EC17
    [InlineData(0, BookRefusal.SlotInPast)]    // EC15
    [InlineData(1, BookRefusal.None)]          // EC16
    [Trait("AC", "AC8"), Trait("EC", "EC15"), Trait("EC", "EC16"), Trait("EC", "EC17")]
    public void Book_SlotStartingAroundNow_IsRefusedOnceStarted(long ticksFromNow, BookRefusal expected)
    {
        _store.Add(new Slot(50, ClinicId, Now.AddTicks(ticksFromNow), 300m));

        var result = _desk.Book(Stranger, Req(Stranger, 50));

        result.Refusal.Should().Be(expected);
        if (expected != BookRefusal.None)
            _store.VisitsForSlot(50).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC8"), Trait("EC", "EC15")]
    public void Book_SlotStartingExactlyNow_IsRefusedWithSlotInPast()
    {
        _store.Add(new Slot(51, ClinicId, Now, 300m));

        var result = _desk.Book(Stranger, Req(Stranger, 51));

        result.Refusal.Should().Be(BookRefusal.SlotInPast);
        result.Visit.Should().BeNull();
    }

    [Fact]
    [Trait("AC", "AC8")]
    public void Book_SlotThatWasFreeWhenListedButHasStartedSince_IsRefusedWithSlotInPast()
    {
        _desk.FreeSlots(ClinicId).Should().Contain(s => s.Id == Tomorrow);
        _time.Advance(TimeSpan.FromDays(1));

        var result = _desk.Book(Stranger, Req(Stranger, Tomorrow));

        result.Refusal.Should().Be(BookRefusal.SlotInPast);
    }

    [Fact]
    [Trait("AC", "AC1"), Trait("AC", "AC8")]
    public void Book_BookedSlot_LeavesFreeSlots()
    {
        BookOk(Stranger, Req(Stranger, Tomorrow));

        _desk.FreeSlots(ClinicId).Select(s => s.Id).Should().BeEquivalentTo([DayAfter, Expensive]);
    }

    // ---------------- AC9: cancel until it starts ----------------

    [Fact]
    [Trait("AC", "AC9")]
    public void Cancel_PatientCancelsBeforeStart_ReturnsTrueAndVisitIsCancelled()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow));

        _desk.Cancel(Stranger, visit.Id).Should().BeTrue();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
    }

    [Fact]
    [Trait("AC", "AC9"), Trait("EC", "EC20")]
    public void Cancel_OneTickBeforeStart_ReturnsTrue()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow));
        _time.SetUtcNow(Now.AddDays(1).AddTicks(-1));

        _desk.Cancel(Stranger, visit.Id).Should().BeTrue();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
    }

    [Theory]
    [InlineData(0)]   // EC21: exactly at the start
    [InlineData(1)]   // a tick after
    [InlineData(TimeSpan.TicksPerHour)]
    [Trait("AC", "AC9"), Trait("EC", "EC21")]
    public void Cancel_AtOrAfterStart_ReturnsFalseAndVisitStaysBooked(long ticksAfterStart)
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow));
        _time.SetUtcNow(Now.AddDays(1).AddTicks(ticksAfterStart));

        _desk.Cancel(Stranger, visit.Id).Should().BeFalse();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    [Trait("AC", "AC9"), Trait("EC", "EC22")]
    public void Cancel_AlreadyCancelled_ReturnsFalse()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow));
        _desk.Cancel(Stranger, visit.Id).Should().BeTrue();

        _desk.Cancel(Stranger, visit.Id).Should().BeFalse();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
    }

    [Theory]
    [InlineData(Alone)]          // a stranger
    [InlineData(OtherGuardian)]  // another family's guardian
    [InlineData(Guardian)]       // a guardian, but not of this patient
    [Trait("AC", "AC9"), Trait("EC", "EC23")]
    public void Cancel_SomeoneElsesVisit_ReturnsFalseAndVisitStaysBooked(int callerId)
    {
        // Given patient 200's booked visit
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow));

        // When someone who is neither 200 nor 200's guardian cancels it
        var cancelled = _desk.Cancel(callerId, visit.Id);

        // Then nothing is cancelled, and the slot stays taken
        cancelled.Should().BeFalse();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
        _desk.FreeSlots(ClinicId).Should().NotContain(s => s.Id == Tomorrow);
    }

    [Fact]
    [Trait("AC", "AC9"), Trait("EC", "EC24")]
    public void Cancel_GuardianCancelsDependentsVisit_ReturnsTrue()
    {
        var visit = BookOk(Guardian, Req(Dependent, Tomorrow));

        _desk.Cancel(Guardian, visit.Id).Should().BeTrue();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
    }

    [Fact]
    [Trait("AC", "AC9")]
    public void Cancel_DependentCancelsVisitTheirGuardianBooked_ReturnsTrue()
    {
        var visit = BookOk(Guardian, Req(Dependent, Tomorrow));

        _desk.Cancel(Dependent, visit.Id).Should().BeTrue();
    }

    [Fact]
    [Trait("AC", "AC9")]
    public void Cancel_DependentCancelsGuardiansOwnVisit_ReturnsFalse()
    {
        var visit = BookOk(Guardian, Req(Guardian, Tomorrow));

        _desk.Cancel(Dependent, visit.Id).Should().BeFalse();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
    }

    [Fact]
    [Trait("AC", "AC9"), Trait("EC", "EC25")]
    public void Cancel_UnknownVisit_ReturnsFalse()
    {
        _desk.Cancel(Stranger, 12345).Should().BeFalse();
    }

    // ---------------- AC10: a cancelled visit frees its slot ----------------

    [Fact]
    [Trait("AC", "AC10")]
    public void Cancel_BookedVisit_SlotIsListedInFreeSlotsAgain()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow));
        _desk.FreeSlots(ClinicId).Should().NotContain(s => s.Id == Tomorrow);

        _desk.Cancel(Stranger, visit.Id).Should().BeTrue();

        _desk.FreeSlots(ClinicId).Should().Contain(s => s.Id == Tomorrow);
    }

    [Fact]
    [Trait("AC", "AC10"), Trait("EC", "EC27")]
    public void Book_SlotFreedByCancel_CanBeBookedByAnotherPatientAndLeavesFreeSlots()
    {
        // Given patient 200 booked tomorrow's slot and cancelled it
        var first = BookOk(Stranger, Req(Stranger, Tomorrow));
        _desk.Cancel(Stranger, first.Id).Should().BeTrue();

        // When patient 400 books the same slot
        var second = BookOk(Alone, Req(Alone, Tomorrow));

        // Then 400 has a new booked visit, 200's stays cancelled, and the slot is taken again
        second.Id.Should().NotBe(first.Id);
        second.Status.Should().Be(VisitStatus.Booked);
        _store.FindVisit(first.Id)!.Status.Should().Be(VisitStatus.Cancelled);
        _desk.FreeSlots(ClinicId).Should().NotContain(s => s.Id == Tomorrow);
    }

    [Fact]
    [Trait("AC", "AC10")]
    public void Book_SlotFreedByCancel_CanBeRebookedBySamePatient()
    {
        var first = BookOk(Stranger, Req(Stranger, Tomorrow));
        _desk.Cancel(Stranger, first.Id).Should().BeTrue();

        var again = _desk.Book(Stranger, Req(Stranger, Tomorrow));

        again.Refusal.Should().Be(BookRefusal.None);
    }

    [Fact]
    [Trait("AC", "AC10"), Trait("AC", "AC11"), Trait("AC", "AC12"), Trait("EC", "EC26")]
    public void Cancel_BookedVisit_GetForPatientAndMineAllShowItCancelled()
    {
        var visit = BookOk(Guardian, Req(Dependent, Tomorrow));

        _desk.Cancel(Guardian, visit.Id).Should().BeTrue();

        _desk.Get(Guardian, visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
        _desk.Get(Dependent, visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
        _desk.ForPatient(Guardian, Dependent).Should().ContainSingle(v => v.Id == visit.Id && v.Status == VisitStatus.Cancelled);
        _desk.Mine(Dependent).Should().ContainSingle(v => v.Id == visit.Id && v.Status == VisitStatus.Cancelled);
    }

    // ---------------- AC11: Get ----------------

    [Fact]
    [Trait("AC", "AC11")]
    public void Get_PatientsOwnVisit_ReturnsIt()
    {
        var visit = BookOk(Stranger, Req(Stranger, Tomorrow, note: "Back pain"));

        _desk.Get(Stranger, visit.Id).Should().BeEquivalentTo(visit);
    }

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC29")]
    public void Get_GuardianAsksForDependentsVisit_ReturnsIt()
    {
        var visit = BookOk(Guardian, Req(Dependent, Tomorrow));

        _desk.Get(Guardian, visit.Id).Should().BeEquivalentTo(visit);
        _desk.Get(Dependent, visit.Id).Should().BeEquivalentTo(visit);
    }

    [Theory]
    [InlineData(Alone)]
    [InlineData(OtherGuardian)]
    [InlineData(OtherDependent)]
    [Trait("AC", "AC11"), Trait("EC", "EC28")]
    public void Get_SomeoneElsesVisit_ReturnsNull(int callerId)
    {
        // Given dependent 101's visit, booked by their guardian 100
        var visit = BookOk(Guardian, Req(Dependent, Tomorrow, note: "Asthma"));

        // When anyone outside that family asks for it by id
        var seen = _desk.Get(callerId, visit.Id);

        // Then they get nothing
        seen.Should().BeNull();
    }

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC30")]
    public void Get_DependentAsksForGuardiansOwnVisit_ReturnsNull()
    {
        var visit = BookOk(Guardian, Req(Guardian, Tomorrow));

        _desk.Get(Dependent, visit.Id).Should().BeNull();
        _desk.Get(Guardian, visit.Id).Should().NotBeNull();
    }

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC31")]
    public void Get_UnknownVisit_ReturnsNull()
    {
        _desk.Get(Stranger, 12345).Should().BeNull();
    }

    // ---------------- AC12: ForPatient ----------------

    [Fact]
    [Trait("AC", "AC12")]
    public void ForPatient_PatientAsksForThemselves_ReturnsTheirVisits()
    {
        var a = BookOk(Stranger, Req(Stranger, Tomorrow));
        var b = BookOk(Stranger, Req(Stranger, DayAfter));
        BookOk(Alone, Req(Alone, Expensive));

        _desk.ForPatient(Stranger, Stranger).Select(v => v.Id).Should().BeEquivalentTo([a.Id, b.Id]);
    }

    [Fact]
    [Trait("AC", "AC12"), Trait("EC", "EC32")]
    public void ForPatient_GuardianAsksForSelfAndDependent_SeesEachPatientsVisits()
    {
        // Given guardian 100 with a visit of their own and dependent 101 with one
        var own = BookOk(Guardian, Req(Guardian, Tomorrow));
        var childs = BookOk(Guardian, Req(Dependent, DayAfter));

        // When the guardian asks for each
        var forSelf = _desk.ForPatient(Guardian, Guardian);
        var forChild = _desk.ForPatient(Guardian, Dependent);

        // Then each list holds that patient's visits and nobody else's
        forSelf.Select(v => v.Id).Should().BeEquivalentTo([own.Id]);
        forChild.Select(v => v.Id).Should().BeEquivalentTo([childs.Id]);
    }

    [Theory]
    [InlineData(Alone, Dependent)]          // a stranger asks for a dependent
    [InlineData(OtherGuardian, Dependent)]  // another family's guardian
    [InlineData(Dependent, Guardian)]       // a dependent asks for their guardian
    [InlineData(Alone, Guardian)]           // a stranger asks for an adult
    [Trait("AC", "AC12"), Trait("EC", "EC33")]
    public void ForPatient_CallerIsNotPatientOrTheirGuardian_ReturnsEmpty(int callerId, int patientId)
    {
        BookOk(Guardian, Req(Guardian, Tomorrow));
        BookOk(Guardian, Req(Dependent, DayAfter));

        _desk.ForPatient(callerId, patientId).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC12"), Trait("EC", "EC34")]
    public void ForPatient_PatientWithNoVisits_ReturnsEmpty()
    {
        _desk.ForPatient(Guardian, Dependent).Should().BeEmpty();
        _desk.ForPatient(Alone, Alone).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC12"), Trait("EC", "EC35")]
    public void ForPatient_IncludesCancelledVisits_AgreeingWithMine()
    {
        var cancelled = BookOk(Guardian, Req(Dependent, Tomorrow));
        _desk.Cancel(Guardian, cancelled.Id).Should().BeTrue();
        var booked = BookOk(Guardian, Req(Dependent, DayAfter));

        var seen = _desk.ForPatient(Guardian, Dependent);

        seen.Select(v => v.Id).Should().BeEquivalentTo([cancelled.Id, booked.Id]);
        seen.Should().BeEquivalentTo(_desk.Mine(Dependent));
    }

    // ---------------- AC13: many users at once ----------------

    [Fact]
    [Trait("AC", "AC13"), Trait("EC", "EC36")]
    public async Task Book_EightPatientsBookSameSlotAtOnce_ExactlyOneVisit()
    {
        // Given a store slow enough that a check-then-add without a lock lets two bookings through
        var store = new SlowStore(_store);
        var desk = new Desk(store, _time);
        var patients = Enumerable.Range(1000, 8).ToArray();
        using var go = new ManualResetEventSlim();

        // When eight patients book tomorrow's slot at the same moment
        var tasks = patients.Select(p => Task.Factory.StartNew(() =>
        {
            go.Wait();
            return desk.Book(p, Req(p, Tomorrow));
        }, TaskCreationOptions.LongRunning)).ToArray();
        go.Set();
        var results = await Task.WhenAll(tasks);

        // Then exactly one has the slot and the other seven are told it's taken
        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().HaveCount(7).And.OnlyContain(r => r.Refusal == BookRefusal.SlotTaken);
        _store.VisitsForSlot(Tomorrow).Where(v => v.Status == VisitStatus.Booked).Should().ContainSingle();
    }

    [Fact]
    [Trait("AC", "AC6"), Trait("AC", "AC13"), Trait("EC", "EC37")]
    public async Task Book_SamePatientUsesPromoOnEightSlotsAtOnce_CodeIsUsedOnce()
    {
        var slots = Enumerable.Range(60, 8).ToArray();
        foreach (var id in slots)
            _store.Add(new Slot(id, ClinicId, Now.AddDays(5).AddHours(id), 300m));
        var desk = new Desk(new SlowStore(_store), _time);
        using var go = new ManualResetEventSlim();

        var tasks = slots.Select(s => Task.Factory.StartNew(() =>
        {
            go.Wait();
            return desk.Book(Stranger, Req(Stranger, s, promo: "SAVE25"));
        }, TaskCreationOptions.LongRunning)).ToArray();
        go.Set();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Ok).Should().Be(1);
        results.Where(r => !r.Ok).Should().OnlyContain(r => r.Refusal == BookRefusal.PromoUsed);
        _store.VisitsOf(Stranger).Where(v => v.PromoCode != null).Should().ContainSingle();
    }

    /// <summary>The real store with a pause on every read, so a race in the desk shows every time.</summary>
    private sealed class SlowStore(IVisitStore inner) : IVisitStore
    {
        private static T Slow<T>(Func<T> read) { Thread.Sleep(20); return read(); }

        public Slot? FindSlot(int id) => Slow(() => inner.FindSlot(id));
        public IReadOnlyList<Slot> SlotsOf(int clinicId) => Slow(() => inner.SlotsOf(clinicId));
        public Promo? FindPromo(string code) => Slow(() => inner.FindPromo(code));
        public IReadOnlyList<int> DependentsOf(int guardianId) => Slow(() => inner.DependentsOf(guardianId));
        public Visit? FindVisit(int id) => Slow(() => inner.FindVisit(id));
        public IReadOnlyList<Visit> VisitsOf(int patientId) => Slow(() => inner.VisitsOf(patientId));
        public IReadOnlyList<Visit> VisitsForSlot(int slotId) => Slow(() => inner.VisitsForSlot(slotId));
        public Visit AddVisit(int patientId, int slotId, int bookedBy, decimal charged, string? promoCode, string note) =>
            inner.AddVisit(patientId, slotId, bookedBy, charged, promoCode, note);
        public void Save(Visit visit) => inner.Save(visit);
    }
}
