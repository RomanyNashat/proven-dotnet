namespace SkillSamples.Compliance;

public sealed class PhiAuditRow
{
    public long Id { get; private set; }                    // bigint: audit tables pass 2 billion rows
    public required string RecordType { get; init; }
    public required int RecordId { get; init; }
    public required string Action { get; init; }            // Added, Modified, Deleted
    public required string ChangedColumns { get; init; }    // names only, never values
    public required string UserId { get; init; }
    public required DateTimeOffset At { get; init; }
}

public interface ICurrentUser
{
    string Id { get; }   // the token's subject
}
