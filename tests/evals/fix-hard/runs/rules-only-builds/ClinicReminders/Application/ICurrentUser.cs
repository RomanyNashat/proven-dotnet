namespace ClinicReminders.Application;

public interface ICurrentUser
{
    /// <summary>The caller's numeric subject (their patient id).</summary>
    int Id { get; }

    /// <summary>Clinic staff may schedule reminders for any patient.</summary>
    bool IsStaff { get; }
}
