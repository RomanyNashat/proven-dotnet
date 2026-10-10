using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PharmacyRefills.Domain;

namespace PharmacyRefills.Infrastructure;

public sealed class RefillQueries(NpgsqlDataSource db, RefillsDbContext context)
{
    public async Task<IReadOnlyList<RefillRow>> SearchAsync(string medication, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync(ct);
        var sql = $"SELECT id, patient_id, medication_name FROM refills WHERE medication_name LIKE '%{medication}%'";
        return (await connection.QueryAsync<RefillRow>(sql)).ToList();
    }

    public Task<List<Refill>> ForPatientAsync(int patientId, CancellationToken ct) =>
        context.Refills
            .FromSql($"SELECT * FROM refills WHERE patient_id = {patientId}")
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<Refill?> FindAsync(int id, CancellationToken ct) =>
        await context.Refills.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Dictionary<int, int>> CountsPerPatientAsync(IEnumerable<int> patientIds, CancellationToken ct)
    {
        var counts = new Dictionary<int, int>();
        foreach (var patientId in patientIds)
            counts[patientId] = await context.Refills.CountAsync(r => r.PatientId == patientId, ct);
        return counts;
    }
}

public sealed record RefillRow(int Id, int PatientId, string MedicationName);
