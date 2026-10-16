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
        AppointmentAt = appointmentAt.ToUniversalTime();
        CreatedAt = createdAt.ToUniversalTime();
        Status = ReminderStatus.Pending;
    }

    public int Id { get; private set; }
    public int PatientId { get; private set; }
    public string Mobile { get; private set; } = "";
    public DateTimeOffset AppointmentAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // Status, AttemptedAt and SentAt change only through DueReminders' set-based updates: the claim has to be
    // a single conditional UPDATE so that two runners can't both win it.
    public ReminderStatus Status { get; private set; }
    public DateTimeOffset? AttemptedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
}

public sealed class AuditEntry
{
    public int Id { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public int PatientId { get; set; }
    public DateTimeOffset At { get; set; }
}
