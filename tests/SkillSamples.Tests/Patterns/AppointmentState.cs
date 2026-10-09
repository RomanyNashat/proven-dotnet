namespace SkillSamples.Patterns;

public enum AppointmentStatus { Booked, CheckedIn, Completed, Cancelled, NoShow }

public enum AppointmentTrigger { CheckIn, Complete, Cancel, MissWindow }

public sealed class Appointment
{
    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Booked;

    // The whole state machine in one place. Anything not listed is not allowed.
    public bool TryApply(AppointmentTrigger trigger)
    {
        AppointmentStatus? next = (Status, trigger) switch
        {
            (AppointmentStatus.Booked, AppointmentTrigger.CheckIn) => AppointmentStatus.CheckedIn,
            (AppointmentStatus.Booked, AppointmentTrigger.Cancel) => AppointmentStatus.Cancelled,
            (AppointmentStatus.Booked, AppointmentTrigger.MissWindow) => AppointmentStatus.NoShow,
            (AppointmentStatus.CheckedIn, AppointmentTrigger.Complete) => AppointmentStatus.Completed,
            _ => null,
        };
        if (next is null)
        {
            return false;
        }

        Status = next.Value;
        return true;
    }
}
