using Microsoft.Extensions.Configuration;
using Npgsql;

namespace ClinicQueue.Application;

/// <summary>Small reads straight from PostgreSQL, without the change tracker.</summary>
public sealed class VisitReads(IConfiguration configuration)
{
    /// <summary>The day of a patient's visit, or null when the visit isn't theirs (or doesn't exist).</summary>
    public async Task<DateTime?> GetDayAsync(int visitId, int patientId, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Queue"));
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT day FROM visit WHERE id = @id AND patient_id = @patient", connection);
        command.Parameters.AddWithValue("id", visitId);
        command.Parameters.AddWithValue("patient", patientId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null ? null : (DateTime)value;
    }
}
