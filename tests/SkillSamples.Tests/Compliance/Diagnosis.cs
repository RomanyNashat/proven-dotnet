namespace SkillSamples.Compliance;

// Marks an entity that holds patient data: every change to it gets an audit row.
public interface IPhiRecord
{
    int Id { get; }
}

public sealed class Diagnosis : IPhiRecord
{
    private Diagnosis() { }   // for EF

    public Diagnosis(int patientId, string code) => (PatientId, Code) = (patientId, code);

    public int Id { get; private set; }
    public int PatientId { get; private set; }
    public string Code { get; private set; } = "";   // ICD-10

    public void Revise(string code) => Code = code;
}
