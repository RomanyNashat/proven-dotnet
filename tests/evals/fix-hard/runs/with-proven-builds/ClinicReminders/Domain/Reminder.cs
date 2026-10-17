namespace ClinicReminders.Domain;

public sealed class Reminder
{
    /// <summary>A reminder the provider refused this many times is left for a person to look at.</summary>
    public const int MaxSendAttempts = 3;

    /// <summary>The SMS goes out for appointments within this window ahead of the run.</summary>
    public static readonly TimeSpan SendAhead = TimeSpan.FromDays(1);

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

    /// <summary>
    /// Set by the run that is about to send, before it calls the provider. The provider takes no idempotency
    /// key, so a claimed reminder whose outcome is unknown (a timeout, a crash) stays claimed and is never
    /// sent again: at most once. Only a refusal from the provider releases it.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; private set; }

    public int Attempts { get; private set; }
}
