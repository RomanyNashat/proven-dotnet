using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ClinicQueue.Application;

/// <summary>Small reads straight from PostgreSQL, without the change tracker.</summary>
public sealed class VisitReads(IConfiguration configuration)
{
    /// <summary>The day of a patient's visit, or null when the visit isn't theirs (or doesn't exist).</summary>
    /// <param name="visitId">The visit.</param>
    /// <param name="patientId">The caller; only their own visits are found.</param>
    /// <param name="ct">Cancels the query.</param>
    /// <returns>The day, or null.</returns>
    public async Task<DateOnly?> GetDayAsync(int visitId, int patientId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Queue"));
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT day FROM visit WHERE id = @id AND patient_id = @patient", connection);
        command.Parameters.AddWithValue("id", visitId);
        command.Parameters.AddWithValue("patient", patientId);
        await using var reader = await command.ExecuteReaderAsync(ct);

        // Ask for the type explicitly: the CLR type Npgsql picks by default for a PostgreSQL date (what
        // ExecuteScalar returned and the old code cast to DateTime) is not stable across major versions.
        return await reader.ReadAsync(ct) ? await reader.GetFieldValueAsync<DateOnly>(0, ct) : null;
    }
}
