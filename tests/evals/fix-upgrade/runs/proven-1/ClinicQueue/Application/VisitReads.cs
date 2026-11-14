using Npgsql;

namespace ClinicQueue.Application;

/// <summary>Small reads straight from PostgreSQL, without the change tracker.</summary>
public sealed class VisitReads(NpgsqlDataSource dataSource)
{
    /// <summary>The day of a patient's visit, or null when the visit isn't theirs (or doesn't exist).</summary>
    public async Task<DateOnly?> GetDayAsync(int visitId, int patientId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT day FROM visit WHERE id = $1 AND patient_id = $2");
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = visitId });
        command.Parameters.Add(new NpgsqlParameter<int> { TypedValue = patientId });
        await using var reader = await command.ExecuteReaderAsync(ct);

        // The type is named: an untyped read of a date column is a DateOnly from Npgsql 10 on, and the old
        // (DateTime) cast threw InvalidCastException.
        return await reader.ReadAsync(ct) ? await reader.GetFieldValueAsync<DateOnly>(0, ct) : null;
    }
}
