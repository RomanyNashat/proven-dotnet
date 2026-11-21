using Microsoft.Extensions.Time.Testing;
using VisitDesk;

namespace HiddenTests;

// Two families and a stranger. Mona (1) is the guardian of Sami (2); Omar (3) is another family's patient.
// Hana (4) was Lina's (5) guardian until yesterday: Lina turned 18.
internal sealed class Clinic
{
    public const int Mona = 1, Sami = 2, Omar = 3, Hana = 4, Lina = 5;
    public const int ClinicId = 7, OtherClinic = 9;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    public InMemoryVisitStore Store { get; } = new();
    public Desk Desk { get; }

    public Clinic(int slots = 4, bool racing = false)
    {
        Store.Add(new GuardianLink(Mona, Sami, Until: null));
        Store.Add(new GuardianLink(Hana, Lina, Until: Time.GetUtcNow().AddDays(-1)));
        Store.Add(new Promo("WELCOME20", 20, ClinicId: null, ValidUntil: null));
        Store.Add(new Promo("ENDED10", 10, ClinicId: null, ValidUntil: Time.GetUtcNow().AddDays(-2)));
        Store.Add(new Promo("NINEONLY", 30, ClinicId: OtherClinic, ValidUntil: null));
        for (var i = 1; i <= slots; i++)
            Store.Add(new Slot(i, ClinicId, Time.GetUtcNow().AddDays(3).AddHours(i), 300m));
        Desk = new Desk(racing ? RaceStore.Around(Store) : Store, Time);
    }

    public Slot AddSlot(int id, TimeSpan fromNow, decimal price = 300m) =>
        Add(new Slot(id, ClinicId, Time.GetUtcNow() + fromNow, price));

    public BookRequest For(int patientId, int slotId, string? promo = null, string? note = null, decimal shown = 300m) =>
        new(patientId, slotId, shown, promo, note);

    private Slot Add(Slot slot) { Store.Add(slot); return slot; }
}
