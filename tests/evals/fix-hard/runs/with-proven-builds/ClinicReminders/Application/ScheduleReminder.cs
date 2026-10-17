namespace ClinicReminders.Application;

/// <param name="PatientId">Optional. The patient is always the caller; a different id is refused.</param>
public sealed record ScheduleReminder(int? PatientId, string Mobile, DateTimeOffset AppointmentAt);

public sealed record ScheduledReminder(int Id);
