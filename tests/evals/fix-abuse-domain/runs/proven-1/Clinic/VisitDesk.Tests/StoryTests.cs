using FluentAssertions;
using Microsoft.Extensions.Time.Testing;

namespace VisitDesk.Tests;

/// <summary>
/// The contract for Book, Get, Cancel and ForPatient, from story.md (criteria AC1..AC17, edges EC1..EC38).
/// Written before the code: tdd-guide makes them pass and doesn't edit them.
/// </summary>
public sealed class StoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private const int Clinic = 7;
    private const int OtherClinic = 8;

    // People. Ali is the guardian of Sara and Omar. Huda is another family's guardian, of Yusuf.
    private const int Ali = 1;
    private const int Sara = 2;
    private const int Omar = 3;
    private const int Huda = 5;
    private const int Yusuf = 6;
    private const int Stranger = 9;

    private const int SlotA = 10;
    private const int SlotB = 11;
    private const int SlotC = 12;
    private const decimal Price = 300m;

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryVisitStore _store = new();
    private readonly Desk _desk;

    public StoryTests()
    {
        _store.Add(new Slot(SlotA, Clinic, Now.AddDays(1), Price));
        _store.Add(new Slot(SlotB, Clinic, Now.AddDays(2), Price));
        _store.Add(new Slot(SlotC, Clinic, Now.AddDays(3), Price));
        _store.Add(new GuardianLink(Ali, Sara, Until: null));
        _store.Add(new GuardianLink(Ali, Omar, Until: null));
        _store.Add(new GuardianLink(Huda, Yusuf, Until: null));
        _store.Add(new Promo("SAVE10", 10, ClinicId: null, ValidUntil: null));
        _desk = new Desk(_store, _time);
    }

    private BookResult Book(int caller, int patient, int slot = SlotA, string? promo = null, string? note = null,
        decimal shown = Price) => _desk.Book(caller, new BookRequest(patient, slot, shown, promo, note));

    private Visit BookOk(int caller, int patient, int slot = SlotA, string? promo = null)
    {
        var result = Book(caller, patient, slot, promo);
        result.Ok.Should().BeTrue("the setup booking must succeed, it was refused {0}", result.Refusal);
        return result.Visit!;
    }

    private void ShouldHaveBookedNothing()
    {
        _store.VisitsForSlot(SlotA).Should().BeEmpty();
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == SlotA);
    }

    // ---------------- AC1: book for yourself ----------------

    [Fact]
    [Trait("AC", "AC1")]
    public void Book_PatientBooksFreeSlotForSelf_ReturnsOkWithTheirVisit()
    {
        var result = Book(Ali, Ali);

        result.Ok.Should().BeTrue();
        result.Refusal.Should().Be(BookRefusal.None);
        result.Visit.Should().NotBeNull();
        result.Visit!.PatientId.Should().Be(Ali);
        result.Visit.BookedBy.Should().Be(Ali);
        result.Visit.SlotId.Should().Be(SlotA);
        result.Visit.Status.Should().Be(VisitStatus.Booked);
        _store.VisitsOf(Ali).Should().ContainSingle(v => v.Id == result.Visit.Id);
        _desk.FreeSlots(Clinic).Should().NotContain(s => s.Id == SlotA);
    }

    // ---------------- AC2: book for a dependent ----------------

    [Fact]
    [Trait("AC", "AC2")]
    public void Book_GuardianBooksForDependentWithOpenLink_VisitIsForDependentBookedByGuardian()
    {
        var result = Book(Ali, Sara);

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(Sara);
        result.Visit.BookedBy.Should().Be(Ali);
        _store.VisitsOf(Sara).Should().ContainSingle();
        _store.VisitsOf(Ali).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC2"), Trait("EC", "EC1")]
    public void Book_GuardianLinkEndedATickAgo_RefusedNotYourPatient()
    {
        // Given Layla, whose guardian link to Ali ended a tick ago (she turned 18, or a court order)
        const int layla = 4;
        _store.Add(new GuardianLink(Ali, layla, Until: Now.AddTicks(-1)));

        // When Ali books a visit for her
        var result = Book(Ali, layla);

        // Then the registry still has the link, but it no longer holds: refused, nothing booked
        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        result.Ok.Should().BeFalse();
        result.Visit.Should().BeNull();
        ShouldHaveBookedNothing();
    }

    [Fact]
    [Trait("AC", "AC2"), Trait("EC", "EC2")]
    public void Book_GuardianLinkEndsAfterTheVisit_IsAllowed()
    {
        const int layla = 4;
        _store.Add(new GuardianLink(Ali, layla, Until: Now.AddDays(30)));

        var result = Book(Ali, layla);

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(layla);
    }

    [Fact]
    [Trait("AC", "AC2"), Trait("EC", "EC3")]
    public void Book_EndedAndNewerOpenLinkForSamePair_OpenLinkHolds()
    {
        const int layla = 4;
        _store.Add(new GuardianLink(Ali, layla, Until: Now.AddYears(-1)));
        _store.Add(new GuardianLink(Ali, layla, Until: null));

        var result = Book(Ali, layla);

        result.Ok.Should().BeTrue();
    }

    // ---------------- AC3: nobody else ----------------

    [Fact]
    [Trait("AC", "AC3")]
    public void Book_ForAnUnrelatedPatient_RefusedNotYourPatient()
    {
        var result = Book(Stranger, Sara);

        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        result.Visit.Should().BeNull();
        ShouldHaveBookedNothing();
        _store.VisitsOf(Sara).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC3"), Trait("EC", "EC4")]
    public void Book_DependentBooksForTheirGuardian_RefusedNotYourPatient()
    {
        var result = Book(Sara, Ali);

        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        ShouldHaveBookedNothing();
    }

    [Fact]
    [Trait("AC", "AC3"), Trait("EC", "EC5")]
    public void Book_GuardianBooksForAnotherFamilysDependent_RefusedNotYourPatient()
    {
        var result = Book(Ali, Yusuf);

        result.Refusal.Should().Be(BookRefusal.NotYourPatient);
        ShouldHaveBookedNothing();
        _store.VisitsOf(Yusuf).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC3"), Trait("EC", "EC37")]
    public void Book_NotYourPatientAndSlotTaken_RefusedNotYourPatientFirst()
    {
        BookOk(Ali, Ali, SlotA);

        var result = Book(Stranger, Sara, SlotA, promo: "NOSUCHCODE");

        result.Refusal.Should().Be(BookRefusal.NotYourPatient, "a stranger must learn nothing about the slot or the code");
    }

    // ---------------- AC4: the price is the slot's ----------------

    [Fact]
    [Trait("AC", "AC4")]
    public void Book_ShownPriceMatchesSlot_ChargedTheSlotsPrice()
    {
        var result = Book(Ali, Ali, shown: Price);

        result.Visit!.Charged.Should().Be(Price);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(299.99)]
    [InlineData(-300)]
    [Trait("AC", "AC4"), Trait("EC", "EC6")]
    public void Book_ShownPriceDiffersFromSlot_ChargedTheSlotsPriceNotTheShownOne(decimal shown)
    {
        var result = Book(Ali, Ali, shown: shown);

        result.Ok.Should().BeTrue();
        result.Visit!.Charged.Should().Be(Price);
        _store.FindVisit(result.Visit.Id)!.Charged.Should().Be(Price);
    }

    [Fact]
    [Trait("AC", "AC4"), Trait("EC", "EC7")]
    public void Book_SlotPriceChangedAfterItWasShown_ChargedTheNewPrice()
    {
        _store.Add(new Slot(SlotA, Clinic, Now.AddDays(1), 350m));

        var result = Book(Ali, Ali, shown: Price);

        result.Visit!.Charged.Should().Be(350m);
    }

    // ---------------- AC5: only a free slot that hasn't started ----------------

    [Fact]
    [Trait("AC", "AC5")]
    public void Book_SlotWithABookedVisit_RefusedSlotTaken()
    {
        BookOk(Ali, Ali, SlotA);

        var result = Book(Ali, Sara, SlotA);

        result.Refusal.Should().Be(BookRefusal.SlotTaken);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(SlotA).Should().ContainSingle();
    }

    [Fact]
    [Trait("AC", "AC5")]
    public void Book_SlotStartedAnHourAgo_RefusedSlotInPast()
    {
        _store.Add(new Slot(20, Clinic, Now.AddHours(-1), Price));

        var result = Book(Ali, Ali, slot: 20);

        result.Refusal.Should().Be(BookRefusal.SlotInPast);
        _store.VisitsForSlot(20).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC5"), Trait("EC", "EC8")]
    public void Book_SlotStartsExactlyNow_RefusedSlotInPast()
    {
        _store.Add(new Slot(20, Clinic, Now, Price));

        var result = Book(Ali, Ali, slot: 20);

        result.Refusal.Should().Be(BookRefusal.SlotInPast, "FreeSlots doesn't list a slot that starts now either");
    }

    [Fact]
    [Trait("AC", "AC5"), Trait("EC", "EC9")]
    public void Book_SlotStartsATickAfterNow_IsBooked()
    {
        _store.Add(new Slot(20, Clinic, Now.AddTicks(1), Price));

        var result = Book(Ali, Ali, slot: 20);

        result.Ok.Should().BeTrue();
    }

    [Fact]
    [Trait("AC", "AC5"), Trait("EC", "EC10")]
    public void Book_SlotWhoseVisitWasCancelled_CanBeBookedAgain()
    {
        var first = BookOk(Ali, Ali, SlotA);
        _desk.Cancel(Ali, first.Id).Should().BeTrue();

        var result = Book(Huda, Yusuf, SlotA);

        result.Ok.Should().BeTrue();
        result.Visit!.PatientId.Should().Be(Yusuf);
    }

    [Fact]
    [Trait("AC", "AC5"), Trait("EC", "EC11")]
    public void Book_SlotThatDoesNotExist_RefusedSlotNotFound()
    {
        var result = Book(Ali, Ali, slot: 999);

        result.Refusal.Should().Be(BookRefusal.SlotNotFound);
        result.Visit.Should().BeNull();
        _store.VisitsOf(Ali).Should().BeEmpty();
    }

    // ---------------- AC6: a promo takes its percent off ----------------

    [Theory]
    [InlineData(10, 270)]
    [InlineData(25, 225)]
    [InlineData(50, 150)]
    [Trait("AC", "AC6")]
    public void Book_WithPromo_ChargedThePriceLessItsPercentAndRecordsTheCode(int percent, decimal charged)
    {
        _store.Add(new Promo("CLINIC" + percent, percent, ClinicId: Clinic, ValidUntil: null));

        var result = Book(Ali, Ali, promo: "CLINIC" + percent);

        result.Ok.Should().BeTrue();
        result.Visit!.Charged.Should().Be(charged);
        result.Visit.PromoCode.Should().BeEquivalentTo("CLINIC" + percent);
    }

    [Fact]
    [Trait("AC", "AC6"), Trait("EC", "EC12")]
    public void Book_PromoTypedInOtherCaseWithSpaces_IsTheSameCode()
    {
        var result = Book(Ali, Ali, promo: "  save10 ");

        result.Ok.Should().BeTrue();
        result.Visit!.Charged.Should().Be(270m);
    }

    // ---------------- AC7: an unknown code refuses ----------------

    [Fact]
    [Trait("AC", "AC7")]
    public void Book_CodeTheClinicDoesNotHave_RefusedUnknownPromo()
    {
        var result = Book(Ali, Ali, promo: "FREEVISIT");

        result.Refusal.Should().Be(BookRefusal.UnknownPromo);
        result.Visit.Should().BeNull();
        ShouldHaveBookedNothing();
    }

    // ---------------- AC8: once per patient ----------------

    [Fact]
    [Trait("AC", "AC8")]
    public void Book_SamePromoTwiceForSamePatient_SecondRefusedPromoUsed()
    {
        BookOk(Ali, Ali, SlotA, promo: "SAVE10");

        var result = Book(Ali, Ali, SlotB, promo: "SAVE10");

        result.Refusal.Should().Be(BookRefusal.PromoUsed);
        result.Visit.Should().BeNull();
        _store.VisitsForSlot(SlotB).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC8"), Trait("EC", "EC13")]
    public void Book_SamePromoForEachDependentAndTheGuardian_UsedOnceByEachPatient()
    {
        var forSara = Book(Ali, Sara, SlotA, promo: "SAVE10");
        var forOmar = Book(Ali, Omar, SlotB, promo: "SAVE10");
        var forAli = Book(Ali, Ali, SlotC, promo: "SAVE10");

        forSara.Visit!.Charged.Should().Be(270m);
        forOmar.Visit!.Charged.Should().Be(270m);
        forAli.Visit!.Charged.Should().Be(270m);
    }

    [Fact]
    [Trait("AC", "AC8"), Trait("EC", "EC14")]
    public void Book_SamePromoTypedInOtherCaseTheSecondTime_RefusedPromoUsed()
    {
        BookOk(Ali, Sara, SlotA, promo: "SAVE10");

        var result = Book(Ali, Sara, SlotB, promo: " Save10");

        result.Refusal.Should().Be(BookRefusal.PromoUsed);
    }

    [Fact]
    [Trait("AC", "AC8"), Trait("AC", "AC11"), Trait("EC", "EC15")]
    public async Task Book_SamePatientBooksTwoSlotsAtOnceWithSamePromo_OnlyOneGetsIt()
    {
        // Given a store whose reads are slow enough for two requests to interleave
        var desk = new Desk(new SlowReads(_store), _time);
        var gate = new ManualResetEventSlim();

        // When Sara's two bookings with SAVE10 arrive at the same moment (a double tap, two phones)
        var a = Task.Run(() => { gate.Wait(); return desk.Book(Ali, new BookRequest(Sara, SlotA, Price, "SAVE10", null)); });
        var b = Task.Run(() => { gate.Wait(); return desk.Book(Ali, new BookRequest(Sara, SlotB, Price, "SAVE10", null)); });
        gate.Set();
        var results = await Task.WhenAll(a, b);

        // Then the code is used once: one booking gets it, the other is refused
        results.Count(r => r.Ok).Should().Be(1);
        results.Should().ContainSingle(r => r.Refusal == BookRefusal.PromoUsed);
        _store.VisitsOf(Sara).Should().ContainSingle(v => v.PromoCode != null);
    }

    // ---------------- AC9: a promo for another clinic, or expired ----------------

    [Fact]
    [Trait("AC", "AC9")]
    public void Book_PromoIssuedForAnotherClinic_RefusedAndNothingBooked()
    {
        _store.Add(new Promo("NORTH20", 20, ClinicId: OtherClinic, ValidUntil: null));

        var result = Book(Ali, Ali, promo: "NORTH20");

        // The refusal code is open (story.md Q2); only that it is refused is asserted.
        result.Ok.Should().BeFalse();
        result.Visit.Should().BeNull();
        ShouldHaveBookedNothing();
    }

    [Fact]
    [Trait("AC", "AC9"), Trait("EC", "EC16")]
    public void Book_PromoForEveryClinicOrThisOne_TakesItsPercentOff()
    {
        _store.Add(new Promo("HERE20", 20, ClinicId: Clinic, ValidUntil: null));

        Book(Ali, Ali, SlotA, promo: "SAVE10").Visit!.Charged.Should().Be(270m);
        Book(Ali, Ali, SlotB, promo: "HERE20").Visit!.Charged.Should().Be(240m);
    }

    [Fact]
    [Trait("AC", "AC9"), Trait("EC", "EC17")]
    public void Book_PromoExpiredADayAgo_RefusedAndNothingBooked()
    {
        _store.Add(new Promo("SUMMER", 30, ClinicId: null, ValidUntil: Now.AddDays(-1)));

        var result = Book(Ali, Ali, promo: "SUMMER");

        result.Ok.Should().BeFalse();
        result.Visit.Should().BeNull();
        ShouldHaveBookedNothing();
    }

    [Fact]
    [Trait("AC", "AC9"), Trait("EC", "EC17")]
    public void Book_PromoValidUntilTomorrow_TakesItsPercentOff()
    {
        _store.Add(new Promo("WINTER", 30, ClinicId: null, ValidUntil: Now.AddDays(1)));

        var result = Book(Ali, Ali, promo: "WINTER");

        result.Ok.Should().BeTrue();
        result.Visit!.Charged.Should().Be(210m);
    }

    // ---------------- AC10: the note ----------------

    [Fact]
    [Trait("AC", "AC10")]
    public void Book_WithNote_VisitKeepsTheNote()
    {
        var result = Book(Ali, Ali, note: "Allergic to penicillin.");

        result.Visit!.Note.Should().Be("Allergic to penicillin.");
        _store.FindVisit(result.Visit.Id)!.Note.Should().Be("Allergic to penicillin.");
    }

    [Fact]
    [Trait("AC", "AC10"), Trait("EC", "EC18")]
    public void Book_NoteOfExactly500Characters_IsKept()
    {
        var note = new string('a', Desk.MaxNoteLength);

        var result = Book(Ali, Ali, note: note);

        result.Ok.Should().BeTrue();
        result.Visit!.Note.Should().Be(note);
    }

    [Fact]
    [Trait("AC", "AC10"), Trait("EC", "EC19")]
    public void Book_NoteOf501Characters_RefusedNoteTooLong()
    {
        var result = Book(Ali, Ali, note: new string('a', Desk.MaxNoteLength + 1));

        result.Refusal.Should().Be(BookRefusal.NoteTooLong);
        result.Visit.Should().BeNull();
        ShouldHaveBookedNothing();
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    [Trait("AC", "AC10"), Trait("EC", "EC20")]
    public void Book_ArabicNoteAroundTheLimit_KeptUpTo500(int letters, bool kept)
    {
        var note = new string('ب', letters); // Arabic letter beh, one UTF-16 unit

        var result = Book(Ali, Ali, note: note);

        result.Ok.Should().Be(kept);
        result.Refusal.Should().Be(kept ? BookRefusal.None : BookRefusal.NoteTooLong);
    }

    [Fact]
    [Trait("AC", "AC10"), Trait("EC", "EC21")]
    public void Book_WithoutNote_StoresAnEmptyNote()
    {
        var result = Book(Ali, Ali, note: null);

        result.Ok.Should().BeTrue();
        result.Visit!.Note.Should().BeEmpty();
    }

    // ---------------- AC11: two at once ----------------

    [Fact]
    [Trait("AC", "AC11")]
    public async Task Book_TwoPatientsBookTheSameSlotAtOnce_OneOkOtherSlotTaken()
    {
        var desk = new Desk(new SlowReads(_store), _time);
        var gate = new ManualResetEventSlim();

        var a = Task.Run(() => { gate.Wait(); return desk.Book(Ali, new BookRequest(Ali, SlotA, Price, null, null)); });
        var b = Task.Run(() => { gate.Wait(); return desk.Book(Huda, new BookRequest(Huda, SlotA, Price, null, null)); });
        gate.Set();
        var results = await Task.WhenAll(a, b);

        results.Should().ContainSingle(r => r.Ok);
        results.Should().ContainSingle(r => r.Refusal == BookRefusal.SlotTaken);
        _store.VisitsForSlot(SlotA).Should().ContainSingle();
    }

    [Fact]
    [Trait("AC", "AC11"), Trait("EC", "EC38")]
    public async Task Book_ManyUsersRaceForOneSlot_ExactlyOneVisitIsBooked()
    {
        // Given one free slot and twenty users on the same desk, as the API runs it
        var desk = new Desk(new SlowReads(_store), _time);
        var gate = new ManualResetEventSlim();
        var users = Enumerable.Range(100, 20).ToArray();

        // When they all book it at the same moment
        var tasks = users.Select(u => Task.Run(() =>
        {
            gate.Wait();
            return desk.Book(u, new BookRequest(u, SlotA, Price, null, null));
        })).ToArray();
        gate.Set();
        var results = await Task.WhenAll(tasks);

        // Then one of them has the slot and every other one is told it is taken
        results.Should().ContainSingle(r => r.Ok);
        results.Count(r => r.Refusal == BookRefusal.SlotTaken).Should().Be(users.Length - 1);
        _store.VisitsForSlot(SlotA).Should().ContainSingle();
    }

    // ---------------- AC12: a refused booking changes nothing ----------------

    [Fact]
    [Trait("AC", "AC12")]
    public void Book_Refused_LeavesNoVisitAndTheSlotFree()
    {
        var result = Book(Ali, Ali, note: new string('x', 600));

        result.Ok.Should().BeFalse();
        ShouldHaveBookedNothing();
        _store.VisitsOf(Ali).Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC12"), Trait("EC", "EC22")]
    public void Book_RefusedForItsNote_PromoItCarriedIsStillUsable()
    {
        Book(Ali, Ali, SlotA, promo: "SAVE10", note: new string('x', 600)).Refusal.Should().Be(BookRefusal.NoteTooLong);

        var retry = Book(Ali, Ali, SlotA, promo: "SAVE10", note: "short");

        retry.Ok.Should().BeTrue();
        retry.Visit!.Charged.Should().Be(270m);
    }

    // ---------------- AC13: Get ----------------

    [Fact]
    [Trait("AC", "AC13")]
    public void Get_OwnVisit_ReturnsIt()
    {
        var visit = BookOk(Ali, Ali);

        _desk.Get(Ali, visit.Id).Should().BeEquivalentTo(visit);
    }

    [Fact]
    [Trait("AC", "AC13")]
    public void Get_DependentsVisitByGuardian_ReturnsIt()
    {
        var visit = BookOk(Ali, Sara);

        _desk.Get(Ali, visit.Id).Should().BeEquivalentTo(visit);
        _desk.Get(Sara, visit.Id).Should().BeEquivalentTo(visit);
    }

    [Fact]
    [Trait("AC", "AC13")]
    public void Get_VisitIdThatDoesNotExist_ReturnsNull()
    {
        _desk.Get(Ali, 999).Should().BeNull();
    }

    [Fact]
    [Trait("AC", "AC13"), Trait("EC", "EC23")]
    public void Get_AnotherUsersVisit_ReturnsNull()
    {
        var visit = BookOk(Ali, Sara);

        _desk.Get(Stranger, visit.Id).Should().BeNull();
        _desk.Get(Huda, visit.Id).Should().BeNull();
        _desk.Get(Omar, visit.Id).Should().BeNull("a sibling is not the patient nor the guardian");
    }

    [Fact]
    [Trait("AC", "AC13"), Trait("EC", "EC24")]
    public void Get_GuardianLinkHasSinceEnded_ReturnsNull()
    {
        // Given Layla's visit, booked by Layla, and a link to Ali that ended last month
        const int layla = 4;
        _store.Add(new GuardianLink(Ali, layla, Until: Now.AddMonths(-1)));
        var visit = BookOk(layla, layla);

        // When Ali asks for it
        var seen = _desk.Get(Ali, visit.Id);

        // Then he sees nothing: the registry still lists the link, but it no longer holds
        seen.Should().BeNull();
        _desk.Get(layla, visit.Id).Should().NotBeNull();
    }

    // ---------------- AC14: cancel until it starts ----------------

    [Fact]
    [Trait("AC", "AC14")]
    public void Cancel_OwnBookedVisitBeforeItStarts_CancelsAndFreesTheSlot()
    {
        var visit = BookOk(Ali, Ali);

        var cancelled = _desk.Cancel(Ali, visit.Id);

        cancelled.Should().BeTrue();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
        _desk.Get(Ali, visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == SlotA);
    }

    [Fact]
    [Trait("AC", "AC14")]
    public void Cancel_GuardianCancelsDependentsVisit_IsCancelled()
    {
        var visit = BookOk(Ali, Sara);

        _desk.Cancel(Ali, visit.Id).Should().BeTrue();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Cancelled);
    }

    [Fact]
    [Trait("AC", "AC14"), Trait("EC", "EC25")]
    public void Cancel_ATickBeforeTheVisitStarts_IsAllowed()
    {
        var visit = BookOk(Ali, Ali);
        _time.SetUtcNow(Now.AddDays(1).AddTicks(-1));

        _desk.Cancel(Ali, visit.Id).Should().BeTrue();
        _store.Refunds.Should().ContainSingle();
    }

    [Fact]
    [Trait("AC", "AC14"), Trait("EC", "EC26")]
    public void Cancel_ExactlyWhenTheVisitStarts_RefusedAndNoRefund()
    {
        var visit = BookOk(Ali, Ali);
        _time.SetUtcNow(Now.AddDays(1));

        _desk.Cancel(Ali, visit.Id).Should().BeFalse();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
        _store.Refunds.Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC14"), Trait("EC", "EC27")]
    public void Cancel_ThenAnotherPatientBooksTheSlot_SlotIsListedAndBookable()
    {
        var visit = BookOk(Ali, Ali);

        _desk.Cancel(Ali, visit.Id).Should().BeTrue();

        _desk.FreeSlots(Clinic).Should().Contain(s => s.Id == SlotA);
        Book(Huda, Huda, SlotA).Ok.Should().BeTrue();
        _store.VisitsForSlot(SlotA).Count(v => v.Status == VisitStatus.Booked).Should().Be(1);
    }

    // ---------------- AC15: the refund ----------------

    [Fact]
    [Trait("AC", "AC15")]
    public void Cancel_BookedVisit_RefundsWhatWasChargedOnce()
    {
        var visit = BookOk(Ali, Ali);

        _desk.Cancel(Ali, visit.Id);

        _store.Refunds.Should().Equal((visit.Id, Price));
    }

    [Fact]
    [Trait("AC", "AC15"), Trait("EC", "EC28")]
    public void Cancel_SlotPriceRoseAfterBooking_RefundsWhatWasCharged()
    {
        // Given Sara's visit, charged 300, and the clinic raising the slot's price to 450 afterwards
        var visit = BookOk(Ali, Sara);
        _store.Add(new Slot(SlotA, Clinic, Now.AddDays(1), 450m));

        // When Ali cancels it
        _desk.Cancel(Ali, visit.Id).Should().BeTrue();

        // Then the refund is the 300 that was paid, not today's 450
        _store.Refunds.Should().Equal((visit.Id, 300m));
    }

    [Fact]
    [Trait("AC", "AC15"), Trait("EC", "EC29")]
    public void Cancel_VisitBookedWithPromo_RefundsTheDiscountedAmount()
    {
        var visit = BookOk(Ali, Ali, promo: "SAVE10");

        _desk.Cancel(Ali, visit.Id).Should().BeTrue();

        _store.Refunds.Should().Equal((visit.Id, 270m));
    }

    // ---------------- AC16: when cancel says no ----------------

    [Fact]
    [Trait("AC", "AC16")]
    public void Cancel_VisitThatStartedAnHourAgo_ReturnsFalseAndNoRefund()
    {
        var visit = BookOk(Ali, Ali);
        _time.SetUtcNow(Now.AddDays(1).AddHours(1));

        _desk.Cancel(Ali, visit.Id).Should().BeFalse();
        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
        _store.Refunds.Should().BeEmpty();
    }

    [Fact]
    [Trait("AC", "AC16"), Trait("EC", "EC30")]
    public void Cancel_Twice_SecondReturnsFalseAndRefundsOnce()
    {
        var visit = BookOk(Ali, Ali);

        _desk.Cancel(Ali, visit.Id).Should().BeTrue();
        _desk.Cancel(Ali, visit.Id).Should().BeFalse();

        _store.Refunds.Should().ContainSingle();
    }

    [Fact]
    [Trait("AC", "AC16"), Trait("EC", "EC31")]
    public async Task Cancel_TwoCancelsAtOnce_RefundsOnce()
    {
        // Given Sara's booked visit, and a store slow enough for two requests to interleave
        var visit = BookOk(Ali, Sara);
        var desk = new Desk(new SlowReads(_store), _time);
        var gate = new ManualResetEventSlim();

        // When Ali and Sara both cancel it at the same moment
        var a = Task.Run(() => { gate.Wait(); return desk.Cancel(Ali, visit.Id); });
        var b = Task.Run(() => { gate.Wait(); return desk.Cancel(Sara, visit.Id); });
        gate.Set();
        var results = await Task.WhenAll(a, b);

        // Then it is cancelled once and refunded once
        results.Count(r => r).Should().Be(1);
        _store.Refunds.Should().Equal((visit.Id, Price));
    }

    [Fact]
    [Trait("AC", "AC16"), Trait("EC", "EC32")]
    public void Cancel_AnotherUsersVisit_ReturnsFalseAndChangesNothing()
    {
        var visit = BookOk(Ali, Sara);

        _desk.Cancel(Stranger, visit.Id).Should().BeFalse();
        _desk.Cancel(Huda, visit.Id).Should().BeFalse();
        _desk.Cancel(Omar, visit.Id).Should().BeFalse();

        _store.FindVisit(visit.Id)!.Status.Should().Be(VisitStatus.Booked);
        _store.Refunds.Should().BeEmpty();
        _desk.FreeSlots(Clinic).Should().NotContain(s => s.Id == SlotA);
    }

    [Fact]
    [Trait("AC", "AC16"), Trait("EC", "EC33")]
    public void Cancel_VisitIdThatDoesNotExist_ReturnsFalse()
    {
        _desk.Cancel(Ali, 999).Should().BeFalse();
        _store.Refunds.Should().BeEmpty();
    }

    // ---------------- AC17: ForPatient ----------------

    [Fact]
    [Trait("AC", "AC17")]
    public void ForPatient_Self_ReturnsTheSameVisitsAsMine()
    {
        BookOk(Ali, Ali, SlotA);
        BookOk(Ali, Ali, SlotB);
        BookOk(Ali, Sara, SlotC);

        var visits = _desk.ForPatient(Ali, Ali);

        visits.Should().HaveCount(2);
        visits.Should().BeEquivalentTo(_desk.Mine(Ali));
    }

    [Fact]
    [Trait("AC", "AC17")]
    public void ForPatient_GuardianAsksForDependent_ReturnsTheDependentsVisits()
    {
        var bySara = BookOk(Sara, Sara, SlotA);
        var byAli = BookOk(Ali, Sara, SlotB);
        BookOk(Ali, Omar, SlotC);

        var visits = _desk.ForPatient(Ali, Sara);

        visits.Select(v => v.Id).Should().BeEquivalentTo([bySara.Id, byAli.Id]);
        visits.Should().BeEquivalentTo(_desk.Mine(Sara));
    }

    [Fact]
    [Trait("AC", "AC17"), Trait("EC", "EC34")]
    public void ForPatient_StrangerAsksForAPatient_ReturnsEmpty()
    {
        BookOk(Ali, Sara);

        _desk.ForPatient(Stranger, Sara).Should().BeEmpty();
        _desk.ForPatient(Huda, Sara).Should().BeEmpty();
        _desk.ForPatient(Sara, Ali).Should().BeEmpty("a dependent is not their guardian's guardian");
    }

    [Fact]
    [Trait("AC", "AC17"), Trait("EC", "EC35")]
    public void ForPatient_GuardianLinkHasEnded_ReturnsEmpty()
    {
        const int layla = 4;
        _store.Add(new GuardianLink(Ali, layla, Until: Now.AddTicks(-1)));
        BookOk(layla, layla);

        _desk.ForPatient(Ali, layla).Should().BeEmpty();
        _desk.ForPatient(layla, layla).Should().ContainSingle();
    }

    [Fact]
    [Trait("AC", "AC17"), Trait("EC", "EC36")]
    public void ForPatient_CancelledVisit_IsListedWithItsStatusLikeMine()
    {
        var visit = BookOk(Ali, Sara);
        _desk.Cancel(Ali, visit.Id).Should().BeTrue();

        var visits = _desk.ForPatient(Ali, Sara);

        visits.Should().ContainSingle(v => v.Id == visit.Id && v.Status == VisitStatus.Cancelled);
        visits.Should().BeEquivalentTo(_desk.Mine(Sara));
    }

    /// <summary>The real store, with reads slow enough that two requests at once interleave between a check and a write.</summary>
    private sealed class SlowReads(InMemoryVisitStore inner) : IVisitStore
    {
        private static T Slow<T>(Func<T> read)
        {
            Thread.Sleep(5);
            return read();
        }

        public Slot? FindSlot(int id) => Slow(() => inner.FindSlot(id));
        public IReadOnlyList<Slot> SlotsOf(int clinicId) => Slow(() => inner.SlotsOf(clinicId));
        public Promo? FindPromo(string code) => Slow(() => inner.FindPromo(code));
        public IReadOnlyList<GuardianLink> LinksOf(int guardianId) => Slow(() => inner.LinksOf(guardianId));
        public Visit? FindVisit(int id) => Slow(() => inner.FindVisit(id));
        public IReadOnlyList<Visit> VisitsOf(int patientId) => Slow(() => inner.VisitsOf(patientId));
        public IReadOnlyList<Visit> VisitsForSlot(int slotId) => Slow(() => inner.VisitsForSlot(slotId));
        public Visit AddVisit(int patientId, int slotId, int bookedBy, decimal charged, string? promoCode, string note) =>
            inner.AddVisit(patientId, slotId, bookedBy, charged, promoCode, note);
        public void Save(Visit visit) => inner.Save(visit);
        public void Refund(int visitId, decimal amount) => inner.Refund(visitId, amount);
    }
}
