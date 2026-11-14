namespace ClinicQueue.Domain;

public sealed class Visit
{
    public Visit(int clinicId, int patientId, DateOnly day, int position)
    {
        ClinicId = clinicId;
        PatientId = patientId;
        Day = day;
        Position = position;
    }

    public int Id { get; private set; }
    public int ClinicId { get; private set; }
    public int PatientId { get; private set; }
    public DateOnly Day { get; private set; }
    public int Position { get; private set; }
}
