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

    // Sending changes these three with conditional set-based updates (ReminderDispatch), not through the
    // entity: the claim must be one atomic UPDATE so that two runs or two pods can't both send the SMS.

    /// <summary>When the SMS provider accepted the message.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When a run took the reminder to send it. Set and never sent means the outcome is unknown.</summary>
    public DateTimeOffset? ClaimedAt { get; private set; }

    /// <summary>How many times a run has claimed it; capped so a permanent failure stops retrying.</summary>
    public int Attempts { get; private set; }
}

public sealed class AuditEntry
{
    public int Id { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public int PatientId { get; set; }
    public DateTimeOffset At { get; set; }
}
