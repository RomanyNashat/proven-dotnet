namespace SkillSamples.Contracts.V2;

// One release later: a status the installed apps have never seen, and a room they don't know about.
public enum VisitStatus
{
    Booked,
    Done,
    Cancelled,
}

public sealed record Visit(int Id, VisitStatus Status, string Doctor, string? Room);

public sealed record NewVisit(int PatientId, string? Note);
