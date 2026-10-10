namespace SkillSamples.Compliance;

public sealed class Consent
{
    private Consent() { }   // for EF

    public int Id { get; private set; }
    public int SubjectId { get; private set; }               // the patient, not the user acting on the record
    public string Purpose { get; private set; } = "";        // treatment, research, marketing
    public DateTimeOffset GrantedAt { get; private set; }
    public DateTimeOffset? WithdrawnAt { get; private set; }

    public static Consent Grant(int subjectId, string purpose, TimeProvider time) =>
        new() { SubjectId = subjectId, Purpose = purpose, GrantedAt = time.GetUtcNow() };

    public void Withdraw(TimeProvider time) => WithdrawnAt ??= time.GetUtcNow();
}
