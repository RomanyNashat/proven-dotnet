using Npgsql;

namespace ClinicQueue.Application;

/// <summary>Small reads straight from PostgreSQL, without the change tracker.</summary>
public sealed class VisitReads(NpgsqlDataSource dataSource)
{
    /// <summary>The day of a patient's visit, or null when the visit isn't theirs (or doesn't exist).</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="patientId">The caller; only their own visit is returned.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The day, or null.</returns>
    public async Task<DateOnly?> GetDayAsync(int visitId, int patientId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT day FROM visit WHERE id = @id AND patient_id = @patient");
        command.Parameters.Add(new NpgsqlParameter<int>("id", visitId));
        command.Parameters.Add(new NpgsqlParameter<int>("patient", patientId));
        await using var reader = await command.ExecuteReaderAsync(ct);

        // The type is named: Npgsql 10 reads an untyped date as DateOnly, where 8 gave DateTime.
        return await reader.ReadAsync(ct) ? await reader.GetFieldValueAsync<DateOnly>(0, ct) : null;
    }
}
