namespace PharmacyRefills.Domain;

public sealed class Refill
{
    public int Id { get; private set; }
    public int PatientId { get; private set; }
    public string MedicationName { get; private set; } = "";
    public string? PharmacistNotes { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public bool Dispensed { get; private set; }

    public static Refill Request(int patientId, string medication, DateTimeOffset now) =>
        new() { PatientId = patientId, MedicationName = medication, RequestedAt = now };

    public void Dispense(string? notes)
    {
        Dispensed = true;
        PharmacistNotes = notes;
    }
}
