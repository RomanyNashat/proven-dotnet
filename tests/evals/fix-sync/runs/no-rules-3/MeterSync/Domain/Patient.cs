namespace MeterSync.Domain;

public sealed class Patient(string nationalId, string phone)
{
    public int Id { get; private set; }

    public string NationalId { get; private set; } = nationalId;

    public string Phone { get; private set; } = phone;
}
