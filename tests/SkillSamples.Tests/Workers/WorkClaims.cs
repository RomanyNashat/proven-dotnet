namespace SkillSamples.Workers;

// Each pod claims a batch of pending rows; a row another pod holds is skipped, not waited on.
// A service has one engine and keeps one statement.
public static class WorkClaims
{
    public const string PostgreSql = """
        UPDATE work_items SET claimed_by = @pod
        WHERE id IN (
            SELECT id FROM work_items
            WHERE status = 'Pending' AND claimed_by IS NULL
            ORDER BY id
            LIMIT @n
            FOR UPDATE SKIP LOCKED)
        RETURNING id
        """;

    public const string SqlServer = """
        UPDATE TOP (@n) work_items WITH (UPDLOCK, READPAST, ROWLOCK)
        SET claimed_by = @pod
        OUTPUT inserted.id
        WHERE status = 'Pending' AND claimed_by IS NULL
        """;
}
