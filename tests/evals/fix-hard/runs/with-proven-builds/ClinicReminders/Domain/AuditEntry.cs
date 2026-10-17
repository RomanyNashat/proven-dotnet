namespace ClinicReminders.Domain;

public sealed class AuditEntry
{
    public int Id { get; set; }
    public int ActorId { get; set; }
    public string Action { get; set; } = "";
    public int PatientId { get; set; }
    public DateTimeOffset At { get; set; }
}
