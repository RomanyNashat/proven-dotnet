namespace ClinicReminders.Application;

public interface ICurrentUser
{
    /// <summary>The caller's patient id, or null when the token carries no usable subject.</summary>
    int? Id { get; }
}
