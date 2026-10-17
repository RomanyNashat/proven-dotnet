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
    public ReminderStatus Status { get; private set; } = ReminderStatus.Pending;

    /// <summary>When a job run claimed the reminder to send it. Set together with <see cref="ReminderStatus.Sending"/>.</summary>
    public DateTimeOffset? ClaimedAt { get; private set; }

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
