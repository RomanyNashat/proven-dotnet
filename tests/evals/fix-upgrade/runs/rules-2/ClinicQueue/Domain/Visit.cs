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

    // The column is a PostgreSQL `date`; DateOnly is what Npgsql maps it to, with no time or kind to get wrong.
    public DateOnly Day { get; private set; }
    public int Position { get; private set; }
}
