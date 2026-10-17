namespace ClinicReminders.Domain;

public sealed class Reminder
{
    private Reminder()
    {
    }

    public Reminder(int patientId, string mobile, DateTimeOffset appointmentAt, DateTimeOffset createdAt)
    {
        PatientId = patientId;
        Mobile = mobile;
        AppointmentAt = appointmentAt;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public int PatientId { get; private set; }
    public string Mobile { get; private set; } = "";
    public DateTimeOffset AppointmentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public void MarkSent(DateTimeOffset at) => SentAt = at;
}

public sealed class AuditEntry
{
    public int Id { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public int PatientId { get; set; }
    public DateTimeOffset At { get; set; }
}
