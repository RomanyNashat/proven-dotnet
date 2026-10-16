namespace SkillSamples.Contracts.V1;

public enum VisitStatus
{
    Booked,
    Done,
}

public sealed record Visit(int Id, VisitStatus Status, string Doctor);

public sealed record NewVisit(int PatientId, string? Note);
