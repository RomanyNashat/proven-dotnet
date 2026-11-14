namespace ClinicQueue.Domain;

public sealed class Visit
{
    public Visit(int clinicId, int patientId, DateTime day, int position)
    {
        ClinicId = clinicId;
        PatientId = patientId;
        Day = day.Date;
        Position = position;
    }

    public int Id { get; private set; }
    public int ClinicId { get; private set; }
    public int PatientId { get; private set; }
    public DateTime Day { get; private set; }
    public int Position { get; private set; }
}
