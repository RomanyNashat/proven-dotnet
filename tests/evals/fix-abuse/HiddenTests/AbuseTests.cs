using VisitDesk;

namespace HiddenTests;

// What a user who wants to cheat, steal or forge would try. task.md doesn't list any of these.
public sealed class AbuseTests
{
    [Fact]
    public void A01_Thief_GetsAnotherFamilysVisitById_SeesNothing()
    {
        var c = new Clinic();
        var omars = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1)).Visit!;

        Assert.Null(c.Desk.Get(Clinic.Mona, omars.Id));
    }

    [Fact]
    public void A02_Thief_CancelsAnotherFamilysVisit_IsRefused()
    {
        var c = new Clinic();
        var omars = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1)).Visit!;

        Assert.False(c.Desk.Cancel(Clinic.Mona, omars.Id));
        Assert.Equal(VisitStatus.Booked, c.Desk.Get(Clinic.Omar, omars.Id)!.Status);
    }

    [Fact]
    public void A03_Thief_ListsAPatientWhoIsntTheirDependent_GetsNothing()
    {
        var c = new Clinic();
        c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1));
        c.Desk.Book(Clinic.Mona, c.For(Clinic.Sami, 2));

        Assert.Empty(c.Desk.ForPatient(Clinic.Mona, Clinic.Omar));
        Assert.Empty(c.Desk.ForPatient(Clinic.Omar, Clinic.Sami));
    }

    [Fact]
    public void A04_Forger_BooksForAPatientWhoIsntTheirDependent_IsRefused()
    {
        var c = new Clinic();

        var result = c.Desk.Book(Clinic.Omar, c.For(Clinic.Sami, 1));

        Assert.False(result.Ok);
        Assert.Empty(c.Store.VisitsOf(Clinic.Sami));
    }

    [Fact]
    public void A05_Forger_SendsALowerShownPrice_IsChargedTheClinicsPrice()
    {
        var c = new Clinic();

        var result = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 1, shown: 1m));

        Assert.True(!result.Ok || result.Visit!.Charged == 300m, $"charged {result.Visit?.Charged}, the price the app sent");
    }

    [Fact]
    public void A06_Cheater_UsesOneCodeOnEightBookingsAtOnce_GetsItOnce()
    {
        for (var round = 0; round < 20; round++)
        {
            var c = new Clinic(slots: 8, racing: true);
            var results = Parallel(8, i => c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, i + 1, promo: "WELCOME20")));

            var discounted = results.Count(r => r.Ok && r.Visit!.Charged < 300m);
            Assert.True(discounted <= 1, $"round {round}: {discounted} bookings got the code's discount");
        }
    }

    [Fact]
    public void A07_Cheater_EightPatientsBookOneSlotAtOnce_OnlyOneGetsIt()
    {
        for (var round = 0; round < 20; round++)
        {
            var c = new Clinic(slots: 1, racing: true);
            var results = Parallel(8, i => c.Desk.Book(100 + i, c.For(100 + i, 1)));

            Assert.True(results.Count(r => r.Ok) == 1, $"round {round}: {results.Count(r => r.Ok)} visits for one slot");
        }
    }

    [Fact]
    public void A08_Forger_BooksASlotThatHasStarted_IsRefused()
    {
        var c = new Clinic();
        c.AddSlot(9, TimeSpan.FromMinutes(-30));

        var result = c.Desk.Book(Clinic.Omar, c.For(Clinic.Omar, 9));

        Assert.False(result.Ok);
        Assert.Empty(c.Desk.Mine(Clinic.Omar));
    }

    private static List<BookResult> Parallel(int n, Func<int, BookResult> book)
    {
        using var start = new Barrier(n);
        var tasks = Enumerable.Range(0, n).Select(i => Task.Factory.StartNew(() =>
        {
            start.SignalAndWait();
            return book(i);
        }, TaskCreationOptions.LongRunning)).ToArray();
        Task.WaitAll(tasks);
        return [.. tasks.Select(t => t.Result)];
    }
}
