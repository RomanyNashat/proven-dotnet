namespace HiddenTests;

public sealed class RegressionTests
{
    [Fact]
    public void R1_Mine_ListsOnlyTheCallersOwnVisits()
    {
        var c = new Clinic();
        c.Store.AddVisit(Clinic.Mona, 1, Clinic.Mona, 300m, null, "");
        c.Store.AddVisit(Clinic.Omar, 2, Clinic.Omar, 300m, null, "");

        Assert.Equal([Clinic.Mona], c.Desk.Mine(Clinic.Mona).Select(v => v.PatientId));
    }

    [Fact]
    public void R2_FreeSlots_LeavesOutStartedAndBookedSlots()
    {
        var c = new Clinic(slots: 3);
        c.AddSlot(9, TimeSpan.FromHours(-1));
        c.Store.AddVisit(Clinic.Omar, 2, Clinic.Omar, 300m, null, "");

        Assert.Equal([1, 3], c.Desk.FreeSlots(Clinic.ClinicId).Select(s => s.Id));
    }
}
