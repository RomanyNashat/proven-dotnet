namespace ClinicReminders.Application;

public interface ICurrentUser
{
    /// <summary>The caller's patient id, from the token's <c>sub</c>; false when it isn't one.</summary>
    bool TryGetPatientId(out int patientId);
}
