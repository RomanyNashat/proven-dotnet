using VisitDesk;

namespace HiddenTests;

// Misuse that turns on a fact the data carries and task.md never names: a link that ended, a code's clinic
// and end date, the stored code, what was paid. None of these is a textbook check.
public sealed class DomainAbuseTests
{
    [Fact]
    public void D01_Thief_AGuardianWhoseLinkEnded_NoLongerActsForTheDependent()
    {
        var c = new Clinic();
        var linas = c.Desk.Book(Clinic.Lina, c.For(Clinic.Lina, 1)).Visit!;

        Assert.False(c.Desk.Book(Clinic.Hana, c.For(Clinic.Lina, 2)).Ok);
        Assert.Empty(c.Desk.ForPatient(Clinic.Hana, Clinic.Lina));
        Assert.Null(c.Desk.Get(Clinic.Hana, linas.Id));
    }

    [Fact]
    public void D02_Thief_AVisitTheGuardianBooked_IsNotTheirsOnceTheLinkEnds()
    {
        var c = new Clinic();
        c.Store.Add(new GuardianLink(Clinic.Omar, 6, Until: c.Time.GetUtcNow().AddDays(1)));
        var booked = c.Desk.Book(Clinic.Omar, c.For(6, 1)).Visit!;

        c.Time.Advance(TimeSpan.FromDays(2));

        Assert.Null(c.Desk.Get(Clinic.Omar, booked.Id));
        Assert.False(c.Desk.Cancel(Clinic.Omar, booked.Id));
        Assert.Equal(VisitStatus.Booked, c.Desk.Get(6, booked.Id)!.Status);
    }

    [Fact]
    public void D03_Cheater_ACodeThatHasEnded_TakesNothingOff()
    {
        var c = new Clinic();
        var result = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, promo: "ENDED10"));

        Assert.True(!result.Ok || result.Visit!.Charged == 300m, $"charged {result.Visit?.Charged} with an ended code");
    }

    [Fact]
    public void D04_Cheater_ACodeIssuedForAnotherClinic_TakesNothingOffHere()
    {
        var c = new Clinic();
        var result = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, promo: "NINEONLY"));

        Assert.True(!result.Ok || result.Visit!.Charged == 300m, $"charged {result.Visit?.Charged} with another clinic's code");
    }

    [Fact]
    public void D05_Cheater_TheSameCodeTypedDifferently_IsStillUsed()
    {
        var c = new Clinic();
        c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, promo: "WELCOME20"));

        var again = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 2, promo: " welcome20 "));

        Assert.True(!again.Ok || again.Visit!.Charged == 300m, "a code typed in lower case with spaces went a second time");
    }

    [Fact]
    public void D06_Cheater_ACodeTheGuardianUsedForTheChild_IsTheChildsUse()
    {
        var c = new Clinic();
        c.Desk.Book(Clinic.Mona, c.For(Clinic.Sami, 1, promo: "WELCOME20"));

        var again = c.Desk.Book(Clinic.Sami, c.For(Clinic.Sami, 2, promo: "WELCOME20"));

        Assert.True(!again.Ok || again.Visit!.Charged == 300m, "one patient got the code twice, once per person who booked");
    }

    [Fact]
    public void D07_Cheater_ARefundIsWhatWasPaid_NotTheSlotsPriceToday()
    {
        var c = new Clinic();
        var visit = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, promo: "WELCOME20")).Visit!;
        c.Store.Add(c.Store.FindSlot(1)! with { Price = 400m });

        c.Desk.Cancel(Clinic.Omar, visit.Id);

        Assert.Equal(240m, c.Store.Refunds.Where(r => r.VisitId == visit.Id).Sum(r => r.Amount));
    }

    [Fact]
    public void D08_Cheater_TwoCancelsAtOnce_RefundOnce()
    {
        for (var round = 0; round < 20; round++)
        {
            var c = new Clinic(racing: true);
            var visit = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1)).Visit!;

            Parallel(4, _ => c.Desk.Cancel(Clinic.Omar, visit.Id));

            var refunded = c.Store.Refunds.Where(r => r.VisitId == visit.Id).Sum(r => r.Amount);
            Assert.True(refunded == 300m, $"round {round}: refunded {refunded} for a 300 visit");
        }
    }

    private static void Parallel(int n, Func<int, bool> act)
    {
        using var start = new Barrier(n);
        var tasks = Enumerable.Range(0, n).Select(i => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait();
            return act(i);
        }, TaskCreationOptions.LongRunning)).ToArray();
        Task.WaitAll(tasks);
    }
}
