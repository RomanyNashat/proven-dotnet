using VisitDesk;

namespace HiddenTests;

// What the task asks for, as written in task.md.
public sealed class FeatureTests
{
    [Fact]
    public void N01_Book_ForYourself_ChargesTheSlotPriceAndShowsInMine()
    {
        var c = new Clinic();

        var result = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, note: "back pain"));

        Assert.True(result.Ok, result.Refusal.ToString());
        Assert.Equal(300m, result.Visit!.Charged);
        Assert.Equal(VisitStatus.Booked, result.Visit.Status);
        Assert.Contains(c.Desk.Mine(Clinic.Omar), v => v.SlotId == 1);
        Assert.DoesNotContain(c.Desk.FreeSlots(Clinic.ClinicId), s => s.Id == 1);
    }

    [Fact]
    public void N02_Book_APromoCode_TakesItsPercentOffOncePerPatient()
    {
        var c = new Clinic();

        var first = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, promo: "WELCOME20"));
        var second = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 2, promo: "WELCOME20"));

        Assert.True(first.Ok, first.Refusal.ToString());
        Assert.Equal(240m, first.Visit!.Charged);
        Assert.True(!second.Ok || second.Visit!.Charged == 300m, "the code took its percent off twice for one patient");
    }

    [Fact]
    public void N03_Book_ANoteOverFiveHundredCharacters_IsRefused()
    {
        var c = new Clinic();

        var longest = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, note: new string('a', 500)));
        var tooLong = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 2, note: new string('a', 501)));

        Assert.True(longest.Ok, longest.Refusal.ToString());
        Assert.False(tooLong.Ok);
    }

    [Fact]
    public void N04_Cancel_BeforeTheVisitStarts_FreesTheSlot_AndAfterItStarts_IsRefused()
    {
        var c = new Clinic();
        var early = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1)).Visit!;
        var late = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 2)).Visit!;

        Assert.True(c.Desk.Cancel(Clinic.Omar, early.Id));
        Assert.Contains(c.Desk.FreeSlots(Clinic.ClinicId), s => s.Id == 1);

        c.Time.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromHours(2)));
        Assert.False(c.Desk.Cancel(Clinic.Omar, late.Id));
        Assert.Equal(VisitStatus.Booked, c.Desk.Get(Clinic.Omar, late.Id)!.Status);
    }

    [Fact]
    public void N05_Guardian_BooksForADependent_AndSeesTheVisit()
    {
        var c = new Clinic();

        var result = c.Desk.Book(Clinic.Mona, c.For(Clinic.Sami, 1));

        Assert.True(result.Ok, result.Refusal.ToString());
        Assert.Equal(Clinic.Sami, result.Visit!.PatientId);
        Assert.NotNull(c.Desk.Get(Clinic.Mona, result.Visit.Id));
        Assert.Contains(c.Desk.ForPatient(Clinic.Mona, Clinic.Sami), v => v.Id == result.Visit.Id);
        Assert.Contains(c.Desk.ForPatient(Clinic.Sami, Clinic.Sami), v => v.Id == result.Visit.Id);
    }

    [Fact]
    public void N06_Book_AnUnknownPromoCode_IsRefused()
    {
        var c = new Clinic();

        var result = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, promo: "NOSUCHCODE"));

        Assert.False(result.Ok);
        Assert.Empty(c.Desk.Mine(Clinic.Omar));
    }
}
