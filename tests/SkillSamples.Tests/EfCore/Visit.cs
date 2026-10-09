namespace SkillSamples.EfCore;

/// <summary>Set by <see cref="AuditInterceptor"/>, never by the code that changes the entity.</summary>
public interface IAudited
{
    DateTimeOffset CreatedAt { get; }
    int CreatedBy { get; }
    DateTimeOffset? UpdatedAt { get; }
    int? UpdatedBy { get; }
}

public sealed class Visit : IAudited
{
    private Visit()
    {
    }

    public Visit(int patientId, string notes)
    {
        PatientId = patientId;
        Notes = notes;
    }

    public int Id { get; private set; }
    public int PatientId { get; private set; }
    public string Notes { get; private set; } = "";
    public bool IsDeleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int CreatedBy { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public int? UpdatedBy { get; private set; }

    public void Amend(string notes) => Notes = notes;

    public void Delete() => IsDeleted = true;
}
